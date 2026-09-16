using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Checkouts;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Checkouts;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Products;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using ProductEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Products.Product;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Vidi ICheckoutService za domensku napomenu. Svaka mutacija zaključava Checkout redak (SELECT ... FOR UPDATE
/// preko ICheckoutHandler.GetForUpdate) unutar transakcije prije ponovnog čitanja/validacije — isti obrazac kao
/// PaymentService (staro)/BookingService.EnsureGroupCapacityAvailable (concurrency, vidi spec section 34/59-61).
/// </summary>
public class CheckoutService : ICheckoutService
{
    private readonly ICheckoutHandler _checkoutHandler;
    private readonly ICheckoutAuditLogHandler _auditLogHandler;
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly IClientHandler _clientHandler;
    private readonly ICompanyHandler _companyHandler;
    private readonly IPackageHandler _packageHandler;
    private readonly IProductHandler _productHandler;
    private readonly IStockLedgerService _stockLedgerService;
    private readonly IPricingService _pricingService;
    private readonly ICommissionLedgerService _commissionLedgerService;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    public CheckoutService(
        ICheckoutHandler checkoutHandler,
        ICheckoutAuditLogHandler auditLogHandler,
        IAppointmentHandler appointmentHandler,
        IClientHandler clientHandler,
        ICompanyHandler companyHandler,
        IPackageHandler packageHandler,
        IProductHandler productHandler,
        IStockLedgerService stockLedgerService,
        IPricingService pricingService,
        ICommissionLedgerService commissionLedgerService,
        IUnitOfWorkFactory unitOfWorkFactory)
    {
        _checkoutHandler = checkoutHandler;
        _auditLogHandler = auditLogHandler;
        _appointmentHandler = appointmentHandler;
        _clientHandler = clientHandler;
        _companyHandler = companyHandler;
        _packageHandler = packageHandler;
        _productHandler = productHandler;
        _stockLedgerService = stockLedgerService;
        _pricingService = pricingService;
        _commissionLedgerService = commissionLedgerService;
        _unitOfWorkFactory = unitOfWorkFactory;
    }

    public async Task<CheckoutDto> Create(Guid organizationId, Guid userId, CheckoutCreateRequest request)
    {
        Client client = await _clientHandler.GetByIdLight(organizationId, request.ClientId);
        if (client == null)
            throw new NotFoundAppException("Client", request.ClientId);
        if (client.IsAnonymized)
            throw new BusinessRuleException(ErrorCodes.ClientAnonymized, "Klijent je anonimiziran — checkout se ne može otvoriti.");
        if (!client.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveClient, "Klijent nije aktivan.");

        Company company = await _companyHandler.GetById(organizationId, request.CompanyId);
        if (company == null)
            throw new NotFoundAppException("Company", request.CompanyId);
        if (!company.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveCompany, $"Tvrtka '{company.Name}' nije aktivna.");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Checkout checkout = new Checkout
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            CompanyId = request.CompanyId,
            ClientId = request.ClientId,
            Status = CheckoutStatus.Open,
            CreatedAt = now,
            CreatedBy = userId
        };

        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();
        await _checkoutHandler.Add(uow, checkout);
        await _auditLogHandler.Add(uow, new CheckoutAuditLog
        {
            Id = Guid.NewGuid(),
            CheckoutId = checkout.Id.GetValueOrDefault(),
            ChangeType = "CheckoutCreated",
            ChangedAt = now,
            ChangedBy = userId
        });
        await uow.CommitAsync();

        return await GetById(organizationId, checkout.Id.GetValueOrDefault());
    }

    public async Task<CheckoutDto> GetById(Guid organizationId, Guid id)
    {
        Checkout checkout = await _checkoutHandler.GetGraph(organizationId, id);
        if (checkout == null)
            throw new NotFoundAppException("Checkout", id);

        return ToDto(checkout);
    }

    public async Task<List<CheckoutDto>> GetByClient(Guid organizationId, Guid clientId)
    {
        List<Checkout> checkouts = await _checkoutHandler.GetByClient(organizationId, clientId);
        return checkouts.Select(ToDto).ToList();
    }

    public async Task<CheckoutDto> AddBookingItem(Guid organizationId, Guid userId, Guid checkoutId, CheckoutAddBookingItemRequest request)
    {
        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

        Checkout locked = await LockOpenCheckout(uow, organizationId, checkoutId);

        Booking booking = await _appointmentHandler.GetBookingById(uow, organizationId, request.BookingId);
        if (booking == null)
            throw new NotFoundAppException("Booking", request.BookingId);

        if (booking.ClientId != locked.ClientId)
            throw new BusinessRuleException(ErrorCodes.CheckoutItemClientMismatch, "Booking pripada drugom klijentu.");

        if (booking.Appointment.CompanyId != locked.CompanyId)
            throw new BusinessRuleException(ErrorCodes.CheckoutItemCompanyMismatch, "Booking pripada drugoj tvrtki.");

        if (booking.Status == BookingStatus.Cancelled)
            throw new BusinessRuleException(ErrorCodes.CheckoutItemNotEligible, "Otkazan booking se ne može dodati u checkout.");

        bool alreadyLocked = await uow.Context.CheckoutItems
            .AnyAsync(i => i.OrganizationId == organizationId && i.BookingId == booking.Id && i.LocksBooking);
        if (alreadyLocked)
            throw new BusinessRuleException(
                ErrorCodes.BookingAlreadyInOpenCheckout, "Booking je već aktivna stavka u drugom otvorenom checkoutu.");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        CheckoutItem item = new CheckoutItem
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            CheckoutId = checkoutId,
            Type = CheckoutItemType.Booking,
            Description = booking.Appointment.Service?.Name ?? "Booking",
            UnitPrice = booking.Amount,
            Quantity = 1,
            Amount = booking.Amount,
            BookingId = booking.Id,
            LocksBooking = true,
            CreatedAt = now,
            CreatedBy = userId
        };

        try
        {
            await _checkoutHandler.AddItem(uow, item);
        }
        catch (DbUpdateException)
        {
            throw new BusinessRuleException(
                ErrorCodes.BookingAlreadyInOpenCheckout, "Booking je već aktivna stavka u drugom otvorenom checkoutu.");
        }

        await _auditLogHandler.Add(uow, new CheckoutAuditLog
        {
            Id = Guid.NewGuid(),
            CheckoutId = checkoutId,
            ChangeType = "ItemAdded",
            NewValue = $"Booking:{booking.Id}",
            ChangedAt = now,
            ChangedBy = userId
        });
        await uow.CommitAsync();

        return await GetById(organizationId, checkoutId);
    }

    public async Task<CheckoutDto> AddPackageItem(Guid organizationId, Guid userId, Guid checkoutId, CheckoutAddPackageItemRequest request)
    {
        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

        Checkout locked = await LockOpenCheckout(uow, organizationId, checkoutId);

        Package package = await _packageHandler.GetById(organizationId, request.PackageId);
        if (package == null)
            throw new NotFoundAppException("Package", request.PackageId);
        if (!package.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactivePackage, $"Paket '{package.Name}' nije aktivan i ne može se prodati.");

        decimal price = await ResolvePackagePrice(organizationId, package.Id.GetValueOrDefault(), locked.CompanyId);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        CheckoutItem item = new CheckoutItem
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            CheckoutId = checkoutId,
            Type = CheckoutItemType.Package,
            Description = package.Name,
            UnitPrice = price,
            Quantity = 1,
            Amount = price,
            PackageId = package.Id,
            LocksBooking = false,
            CreatedAt = now,
            CreatedBy = userId
        };

        await _checkoutHandler.AddItem(uow, item);

        await _auditLogHandler.Add(uow, new CheckoutAuditLog
        {
            Id = Guid.NewGuid(),
            CheckoutId = checkoutId,
            ChangeType = "ItemAdded",
            NewValue = $"Package:{package.Id}",
            ChangedAt = now,
            ChangedBy = userId
        });
        await uow.CommitAsync();

        return await GetById(organizationId, checkoutId);
    }

    public async Task<CheckoutDto> AddProductItem(Guid organizationId, Guid userId, Guid checkoutId, CheckoutAddProductItemRequest request)
    {
        if (request.Quantity <= 0)
            throw new ValidationAppException("Količina mora biti veća od nule.");

        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

        Checkout locked = await LockOpenCheckout(uow, organizationId, checkoutId);

        // Checkout.Company je mogla postati neaktivna nakon što je Checkout otvoren — Create ne jamči da
        // ostane aktivna do trenutka dodavanja stavke, pa se ovdje eksplicitno revalidira (isti obrazac kao
        // _packageHandler.GetById poziv u AddPackageItem, zaseban DbContext dok je Checkout redak zaključan u
        // uow — ne dira zaključani redak niti transakciju, samo čita drugu tablicu). Namjerno SAMO za Product;
        // Booking/Package ostaju izvan dosega ovog popravka.
        Company checkoutCompany = await _companyHandler.GetById(organizationId, locked.CompanyId);
        if (checkoutCompany == null)
            throw new NotFoundAppException("Company", locked.CompanyId);
        if (!checkoutCompany.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveCompany, $"Tvrtka '{checkoutCompany.Name}' nije aktivna.");

        ProductEntity product = await _productHandler.GetById(organizationId, request.ProductId);
        if (product == null)
            throw new NotFoundAppException("Product", request.ProductId);
        if (!product.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveProduct, $"Proizvod '{product.Name}' nije aktivan i ne može se prodati.");

        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Jedna stavka po Productu po Checkoutu — ponovno dodavanje istog Producta povećava Quantity/Amount
        // postojeće aktivne stavke umjesto stvaranja duplikata (vidi spec section 27).
        CheckoutItem existing = await uow.Context.CheckoutItems.SingleOrDefaultAsync(
            i => i.OrganizationId == organizationId && i.CheckoutId == checkoutId &&
                 i.Type == CheckoutItemType.Product && i.ProductId == product.Id);

        if (existing != null)
        {
            (int newQuantity, decimal newAmount) = CalculateProductLine(existing.Quantity, existing.UnitPrice, request.Quantity);
            existing.Quantity = newQuantity;
            existing.Amount = newAmount;
            await uow.Context.SaveChangesAsync();
        }
        else
        {
            (int newQuantity, decimal newAmount) = CalculateProductLine(0, product.DefaultPrice, request.Quantity);
            CheckoutItem item = new CheckoutItem
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                CheckoutId = checkoutId,
                Type = CheckoutItemType.Product,
                Description = product.Name,
                UnitPrice = product.DefaultPrice,
                Quantity = newQuantity,
                Amount = newAmount,
                ProductId = product.Id,
                LocksBooking = false,
                CreatedAt = now,
                CreatedBy = userId
            };

            await _checkoutHandler.AddItem(uow, item);
        }

        await _auditLogHandler.Add(uow, new CheckoutAuditLog
        {
            Id = Guid.NewGuid(),
            CheckoutId = checkoutId,
            ChangeType = "ItemAdded",
            NewValue = $"Product:{product.Id}:{request.Quantity}",
            ChangedAt = now,
            ChangedBy = userId
        });
        await uow.CommitAsync();

        return await GetById(organizationId, checkoutId);
    }

    /// <summary>Zaštićena (checked) aritmetika za (novu ili spojenu) Product stavku — sprječava da zbroj
    /// Quantity tiho prijeđe u negativan preko int overflowa (npr. postojeća Quantity=int.MaxValue + novi unos)
    /// i da Amount = UnitPrice * Quantity ikad tiho perzistira izvan decimal raspona (decimal operatori inače
    /// bacaju sirovi OverflowException, ovdje se prevodi u domenski kod). Ne mijenja UnitPrice snapshot —
    /// pozivatelj uvijek prosljeđuje postojeći (ili Product.DefaultPrice za novu stavku) UnitPrice nepromijenjen.</summary>
    private static (int Quantity, decimal Amount) CalculateProductLine(int currentQuantity, decimal unitPrice, int addedQuantity)
    {
        try
        {
            int quantity = checked(currentQuantity + addedQuantity);
            decimal amount = checked(unitPrice * quantity);
            return (quantity, amount);
        }
        catch (OverflowException)
        {
            throw new BusinessRuleException(
                ErrorCodes.InvalidQuantity, "Količina ili iznos stavke premašuje dopušteni raspon.");
        }
    }

    public async Task<CheckoutDto> RemoveItem(Guid organizationId, Guid userId, Guid checkoutId, Guid itemId)
    {
        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

        await LockOpenCheckout(uow, organizationId, checkoutId);

        CheckoutItem item = await _checkoutHandler.GetItem(uow, organizationId, checkoutId, itemId);
        if (item == null)
            throw new NotFoundAppException("CheckoutItem", itemId);

        bool hasActiveAllocation = item.Allocations.Any(a => a.Payment != null && a.Payment.Status == PaymentStatus.Completed);
        if (hasActiveAllocation)
            throw new BusinessRuleException(
                ErrorCodes.CheckoutItemHasAllocations, "Stavka ima aktivnu novčanu alokaciju — poništite plaćanje prije uklanjanja.");

        await _checkoutHandler.RemoveItem(uow, item);

        await _auditLogHandler.Add(uow, new CheckoutAuditLog
        {
            Id = Guid.NewGuid(),
            CheckoutId = checkoutId,
            ChangeType = "ItemRemoved",
            OldValue = item.Description,
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = userId
        });
        await uow.CommitAsync();

        return await GetById(organizationId, checkoutId);
    }

    public async Task<CheckoutDto> RecordPayment(Guid organizationId, Guid userId, Guid checkoutId, CheckoutPaymentCreateRequest request)
    {
        if (request.Amount <= 0m)
            throw new ValidationAppException("Iznos plaćanja mora biti veći od 0.");

        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

        await LockOpenCheckout(uow, organizationId, checkoutId);
        Checkout graph = await _checkoutHandler.GetGraph(uow, organizationId, checkoutId);

        List<CheckoutItem> orderedItems = graph.Items.OrderBy(i => i.CreatedAt).ToList();
        List<CheckoutItemFinancials> financials = orderedItems.Select(i => CheckoutFinancialsCalculator.CalculateItem(i, i.Booking)).ToList();
        decimal totalOutstanding = financials.Sum(f => f.OutstandingAmount);

        if (request.Amount > totalOutstanding)
            throw new BusinessRuleException(
                ErrorCodes.PaymentExceedsOutstandingAmount, "Iznos premašuje preostali dug checkouta.",
                new { outstanding = totalOutstanding, requested = request.Amount });

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid paymentId = Guid.NewGuid();

        Payment payment = new Payment
        {
            Id = paymentId,
            OrganizationId = organizationId,
            CheckoutId = checkoutId,
            Amount = request.Amount,
            Method = request.Method,
            Status = PaymentStatus.Completed,
            Note = request.Note,
            IsCheckInGenerated = false,
            CreatedAt = now,
            CreatedBy = userId
        };

        payment.Allocations = request.Allocations is { Count: > 0 }
            ? BuildExplicitAllocations(paymentId, request, orderedItems, financials, now)
            : BuildFifoAllocations(paymentId, request.Amount, orderedItems, financials, now);

        await _checkoutHandler.AddPayment(uow, payment);

        await _auditLogHandler.Add(uow, new CheckoutAuditLog
        {
            Id = Guid.NewGuid(),
            CheckoutId = checkoutId,
            ChangeType = "PaymentRecorded",
            NewValue = $"{payment.Amount} {payment.Method}",
            ChangedAt = now,
            ChangedBy = userId
        });
        await uow.CommitAsync();

        return await GetById(organizationId, checkoutId);
    }

    private static List<PaymentAllocation> BuildFifoAllocations(
        Guid paymentId, decimal amount, List<CheckoutItem> orderedItems, List<CheckoutItemFinancials> financials, DateTimeOffset now)
    {
        List<PaymentAllocation> allocations = new List<PaymentAllocation>();
        decimal remaining = amount;

        for (int i = 0; i < orderedItems.Count && remaining > 0m; i++)
        {
            decimal itemOutstanding = financials[i].OutstandingAmount;
            if (itemOutstanding <= 0m)
                continue;

            decimal allocate = Math.Min(remaining, itemOutstanding);
            allocations.Add(new PaymentAllocation
            {
                Id = Guid.NewGuid(),
                PaymentId = paymentId,
                CheckoutItemId = orderedItems[i].Id.GetValueOrDefault(),
                Amount = allocate,
                CreatedAt = now
            });
            remaining -= allocate;
        }

        return allocations;
    }

    private static List<PaymentAllocation> BuildExplicitAllocations(
        Guid paymentId, CheckoutPaymentCreateRequest request, List<CheckoutItem> orderedItems,
        List<CheckoutItemFinancials> financials, DateTimeOffset now)
    {
        if (request.Allocations.Any(a => a.Amount <= 0m))
            throw new ValidationAppException("Svaka alokacija mora biti veća od 0.");

        if (request.Allocations.Sum(a => a.Amount) != request.Amount)
            throw new BusinessRuleException(
                ErrorCodes.AllocationAmountMismatch, "Zbroj alokacija mora biti jednak iznosu plaćanja.");

        Dictionary<Guid, decimal> requestedByItem = request.Allocations
            .GroupBy(a => a.CheckoutItemId)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));

        List<PaymentAllocation> allocations = new List<PaymentAllocation>();

        foreach (KeyValuePair<Guid, decimal> kvp in requestedByItem)
        {
            int index = orderedItems.FindIndex(i => i.Id == kvp.Key);
            if (index < 0)
                throw new NotFoundAppException("CheckoutItem", kvp.Key);

            // Eksplicitna provjera (uz već-ispravan MonetaryDue=0 izračun ispod) da alokacija ne smije ciljati
            // Booking stavku čije je pokriće trenutno namireno paketom — mora ostati isključivo novčano ILI
            // paket-namireno, nikad oboje (vidi spec fix section 2).
            CheckoutItem targetItem = orderedItems[index];
            if (targetItem.Type == CheckoutItemType.Booking && CheckoutFinancialsCalculator.IsBookingPackageSettled(targetItem.Booking))
                throw new BusinessRuleException(
                    ErrorCodes.PaymentNotAllowed, "Booking je pokriven paketom — dodatna novčana naplata nije dopuštena.");

            if (kvp.Value > financials[index].OutstandingAmount)
                throw new BusinessRuleException(
                    ErrorCodes.AllocationExceedsItemOutstanding, "Alokacija premašuje preostali dug stavke.",
                    new { checkoutItemId = kvp.Key, outstanding = financials[index].OutstandingAmount, requested = kvp.Value });

            allocations.Add(new PaymentAllocation
            {
                Id = Guid.NewGuid(),
                PaymentId = paymentId,
                CheckoutItemId = kvp.Key,
                Amount = kvp.Value,
                CreatedAt = now
            });
        }

        return allocations;
    }

    public async Task<CheckoutDto> VoidPayment(Guid organizationId, Guid userId, Guid checkoutId, Guid paymentId, CheckoutPaymentVoidRequest request)
    {
        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

        await LockOpenCheckout(uow, organizationId, checkoutId);

        Payment payment = await _checkoutHandler.GetPayment(uow, organizationId, checkoutId, paymentId);
        if (payment == null)
            throw new NotFoundAppException("Payment", paymentId);
        if (payment.Status == PaymentStatus.Voided)
            throw new BusinessRuleException(ErrorCodes.PaymentAlreadyVoided, "Plaćanje je već poništeno.");

        payment.Status = PaymentStatus.Voided;
        payment.VoidedAt = DateTimeOffset.UtcNow;
        payment.VoidedBy = userId;
        payment.VoidReason = request.Reason;

        await _checkoutHandler.UpdatePayment(uow, payment);

        await _auditLogHandler.Add(uow, new CheckoutAuditLog
        {
            Id = Guid.NewGuid(),
            CheckoutId = checkoutId,
            ChangeType = "PaymentVoided",
            OldValue = $"{payment.Amount} {payment.Method}",
            NewValue = "Voided",
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = userId
        });
        await uow.CommitAsync();

        return await GetById(organizationId, checkoutId);
    }

    public async Task<CheckoutDto> Complete(Guid organizationId, Guid userId, Guid checkoutId)
    {
        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

        await LockOpenCheckout(uow, organizationId, checkoutId, allowedErrorCode: ErrorCodes.AlreadyCompleted,
            allowedErrorMessage: "Checkout je već zatvoren (Completed, Cancelled ili Voided).");

        Checkout graph = await _checkoutHandler.GetGraph(uow, organizationId, checkoutId);
        CheckoutFinancials totals = CheckoutFinancialsCalculator.Calculate(graph);

        if (!totals.IsFullyPaid)
            throw new BusinessRuleException(
                ErrorCodes.CheckoutOutstandingBalance, "Checkout nije u potpunosti namiren.", new { outstanding = totals.OutstandingAmount });

        DateTimeOffset now = DateTimeOffset.UtcNow;

        List<CheckoutItem> productItems = graph.Items.Where(i => i.Type == CheckoutItemType.Product).ToList();
        await _stockLedgerService.ConsumeForSale(uow, organizationId, userId, graph.CompanyId, productItems, now);

        foreach (CheckoutItem item in graph.Items.Where(i => i.Type == CheckoutItemType.Package && i.ClientPackageId == null))
            await IssueClientPackage(uow, organizationId, userId, graph, item, now);

        graph.Status = CheckoutStatus.Completed;
        graph.CompletedAt = now;
        graph.CompletedBy = userId;
        foreach (CheckoutItem item in graph.Items.Where(i => i.LocksBooking))
            item.LocksBooking = false;

        await _checkoutHandler.Update(uow, graph);

        await _auditLogHandler.Add(uow, new CheckoutAuditLog
        {
            Id = Guid.NewGuid(),
            CheckoutId = checkoutId,
            ChangeType = "CheckoutCompleted",
            ChangedAt = now,
            ChangedBy = userId
        });

        // Provizija za Product/Package prodajne stavke se zarađuje ISTOM transakcijom kao completion —
        // prodavatelj se razrješava iz userId preko postojeće Employee.UserId veze (vidi ICommissionLedgerService,
        // spec section 19/27/28).
        await _commissionLedgerService.GenerateForCheckoutCompletion(uow, organizationId, userId, graph);

        await uow.CommitAsync();

        return await GetById(organizationId, checkoutId);
    }

    public async Task<CheckoutDto> Cancel(Guid organizationId, Guid userId, Guid checkoutId)
    {
        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

        await LockOpenCheckout(uow, organizationId, checkoutId);
        Checkout graph = await _checkoutHandler.GetGraph(uow, organizationId, checkoutId);

        bool hasActivePayment = graph.Payments.Any(p => p.Status == PaymentStatus.Completed);
        if (hasActivePayment)
            throw new BusinessRuleException(
                ErrorCodes.CheckoutHasActivePayments, "Checkout ima aktivna plaćanja — poništite ih prije otkazivanja.");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        graph.Status = CheckoutStatus.Cancelled;
        graph.CancelledAt = now;
        graph.CancelledBy = userId;
        foreach (CheckoutItem item in graph.Items.Where(i => i.LocksBooking))
            item.LocksBooking = false;

        await _checkoutHandler.Update(uow, graph);

        await _auditLogHandler.Add(uow, new CheckoutAuditLog
        {
            Id = Guid.NewGuid(),
            CheckoutId = checkoutId,
            ChangeType = "CheckoutCancelled",
            ChangedAt = now,
            ChangedBy = userId
        });
        await uow.CommitAsync();

        return await GetById(organizationId, checkoutId);
    }

    /// <summary>Zaključava Checkout redak i provjerava da je Open — jedinstvena ulazna točka za sve mutacije
    /// (vidi spec section 34/59-61). `allowedErrorCode`/`allowedErrorMessage` dopuštaju pozivatelju (Complete)
    /// prilagoditi poruku za "već zatvoren" slučaj bez duplicirane logike.</summary>
    private async Task<Checkout> LockOpenCheckout(
        IUnitOfWork uow, Guid organizationId, Guid checkoutId, string allowedErrorCode = null, string allowedErrorMessage = null)
    {
        Checkout locked = await _checkoutHandler.GetForUpdate(uow, organizationId, checkoutId);
        if (locked == null)
            throw new NotFoundAppException("Checkout", checkoutId);

        if (locked.Status != CheckoutStatus.Open)
            throw new BusinessRuleException(
                allowedErrorCode ?? ErrorCodes.CheckoutNotOpen,
                allowedErrorMessage ?? "Checkout nije otvoren (Completed, Cancelled ili Voided) — mutacije su dopuštene samo dok je Status=Open.");

        return locked;
    }

    private async Task<decimal> ResolvePackagePrice(Guid organizationId, Guid packageId, Guid companyId)
    {
        ResolvePriceResponse resolved = await _pricingService.ResolvePrice(organizationId, new ResolvePriceRequest
        {
            SubjectType = PricingSubjectType.Package,
            SubjectId = packageId,
            CompanyId = companyId,
            Date = DateTimeOffset.UtcNow
        });
        return resolved.Price;
    }

    private async Task IssueClientPackage(
        IUnitOfWork uow, Guid organizationId, Guid userId, Checkout checkout, CheckoutItem item, DateTimeOffset purchaseDate)
    {
        Package package = await _packageHandler.GetById(organizationId, item.PackageId.GetValueOrDefault());
        if (package == null)
            throw new NotFoundAppException("Package", item.PackageId.GetValueOrDefault());
        if (!package.IsActive)
            throw new BusinessRuleException(
                ErrorCodes.InactivePackage, $"Paket '{package.Name}' više nije aktivan — kupnja se ne može dovršiti.");

        DateTimeOffset expiryDate = PackageExpiryCalculator.CalculateExpiryDate(
            package.ValidityType, purchaseDate, package.ValidityDays, package.ValidityFixedDate);

        Guid clientPackageId = Guid.NewGuid();
        ClientPackage clientPackage = new ClientPackage
        {
            Id = clientPackageId,
            OrganizationId = organizationId,
            ClientId = checkout.ClientId,
            PackageId = package.Id.GetValueOrDefault(),
            PurchaseDate = purchaseDate,
            // Cijena je već snapshotana na CheckoutItem.Amount kod dodavanja — NE re-razrješava se ovdje (vidi
            // spec section 25).
            PaidPrice = item.Amount,
            EntryMode = package.EntryMode,
            TotalEntryCount = package.TotalEntryCount,
            RemainingSharedEntries = package.EntryMode == PackageEntryMode.SharedPool ? package.TotalEntryCount : null,
            ValidityType = package.ValidityType,
            ExpiryDate = expiryDate,
            Status = ClientPackageStatus.Active,
            CreatedAt = purchaseDate,
            CreatedBy = userId
        };

        foreach (PackageServiceItem serviceItem in package.Services)
        {
            clientPackage.ServiceEntries.Add(new ClientPackageServiceEntry
            {
                Id = Guid.NewGuid(),
                ClientPackageId = clientPackageId,
                ServiceId = serviceItem.ServiceId,
                TotalEntries = package.EntryMode == PackageEntryMode.PerService ? serviceItem.EntryCount : null,
                RemainingEntries = package.EntryMode == PackageEntryMode.PerService ? serviceItem.EntryCount : null
            });
        }

        uow.Context.ClientPackages.Add(clientPackage);
        item.ClientPackageId = clientPackageId;

        await _auditLogHandler.Add(uow, new CheckoutAuditLog
        {
            Id = Guid.NewGuid(),
            CheckoutId = checkout.Id.GetValueOrDefault(),
            ChangeType = "ClientPackageIssued",
            NewValue = clientPackageId.ToString(),
            ChangedAt = purchaseDate,
            ChangedBy = userId
        });
    }

    private static CheckoutDto ToDto(Checkout checkout)
    {
        List<CheckoutItem> orderedItems = checkout.Items.OrderBy(i => i.CreatedAt).ToList();
        CheckoutFinancials totals = CheckoutFinancialsCalculator.Calculate(checkout);

        List<CheckoutItemDto> items = orderedItems.Select(item =>
        {
            CheckoutItemFinancials financials = CheckoutFinancialsCalculator.CalculateItem(item, item.Booking);
            return new CheckoutItemDto
            {
                Id = item.Id.GetValueOrDefault(),
                Type = item.Type,
                Description = item.Description,
                UnitPrice = item.UnitPrice,
                Quantity = item.Quantity,
                RetailAmount = financials.RetailAmount,
                MonetaryDue = financials.MonetaryDue,
                PaidAmount = financials.PaidAmount,
                OutstandingAmount = financials.OutstandingAmount,
                BookingId = item.BookingId,
                PackageId = item.PackageId,
                ProductId = item.ProductId,
                ClientPackageId = item.ClientPackageId,
                CreatedAt = item.CreatedAt,
                CreatedBy = item.CreatedBy
            };
        }).ToList();

        return new CheckoutDto
        {
            Id = checkout.Id.GetValueOrDefault(),
            Status = checkout.Status,
            CompanyId = checkout.CompanyId,
            CompanyName = checkout.Company?.Name,
            ClientId = checkout.ClientId,
            ClientName = checkout.Client != null ? $"{checkout.Client.FirstName} {checkout.Client.LastName}" : null,
            Items = items,
            Payments = checkout.Payments.OrderByDescending(p => p.CreatedAt).Select(PaymentDtoFactory.ToDto).ToList(),
            Totals = new CheckoutTotalsDto
            {
                RetailTotal = totals.RetailTotal,
                MonetaryDue = totals.MonetaryDue,
                PaidAmount = totals.PaidAmount,
                OutstandingAmount = totals.OutstandingAmount,
                IsFullyPaid = totals.IsFullyPaid
            },
            CreatedAt = checkout.CreatedAt,
            CreatedBy = checkout.CreatedBy,
            CompletedAt = checkout.CompletedAt,
            CompletedBy = checkout.CompletedBy,
            CancelledAt = checkout.CancelledAt,
            CancelledBy = checkout.CancelledBy
        };
    }
}
