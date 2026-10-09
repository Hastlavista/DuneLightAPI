using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Checkouts;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Checkouts;
using BlueDragon.DuneLight.Core.Interfaces.Groups;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
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
    private readonly IOrganizationCalendarService _organizationCalendarService;
    private readonly IBookingSegmentParticipationHandler _participationHandler;
    private readonly IClientMembershipHandler _membershipHandler;
    private readonly IMembershipCoverageService _membershipCoverage;
    private readonly IGroupMembershipSkipService _membershipSkips;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly TimeProvider _timeProvider;

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
        IUnitOfWorkFactory unitOfWorkFactory,
        IOrganizationCalendarService organizationCalendarService,
        IBookingSegmentParticipationHandler participationHandler,
        IClientMembershipHandler membershipHandler,
        IMembershipCoverageService membershipCoverage,
        IGroupMembershipSkipService membershipSkips,
        IEmployeeHandler employeeHandler,
        TimeProvider timeProvider)
    {
        _membershipCoverage = membershipCoverage;
        _membershipSkips = membershipSkips;
        _employeeHandler = employeeHandler;
        _membershipHandler = membershipHandler;
        _organizationCalendarService = organizationCalendarService;
        _participationHandler = participationHandler;
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
        _timeProvider = timeProvider;
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

        DateTimeOffset now = _timeProvider.GetUtcNow();
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

        // Stavka usluge cilja SUDJELOVANJE (ParticipationId) — nikad "sva" ni "jedino" sudjelovanje Bookinga.
        if (!request.ParticipationId.HasValue)
            throw new ValidationAppException("Stavka usluge zahtijeva ParticipationId.");
        Guid? owningBookingId = await uow.Context.BookingSegmentParticipations
            .Where(p => p.OrganizationId == organizationId && p.Id == request.ParticipationId.Value)
            .Select(p => (Guid?)p.BookingId)
            .SingleOrDefaultAsync();
        if (owningBookingId == null)
            throw new NotFoundAppException("Participation", request.ParticipationId.Value);
        Guid bookingId = owningBookingId.Value;

        Booking booking = await _appointmentHandler.GetBookingById(uow, organizationId, bookingId);
        if (booking == null)
            throw new NotFoundAppException("Booking", bookingId);

        BookingSegmentParticipation participation = BookingParticipations.ById(booking, request.ParticipationId.Value);

        if (booking.ClientId != locked.ClientId)
            throw new BusinessRuleException(ErrorCodes.CheckoutItemClientMismatch, "Booking pripada drugom klijentu.");

        ParticipationExecutionContext execution = ExecutionContextResolver.ForParticipation(booking.Appointment, booking, participation);

        if (execution.CompanyId != locked.CompanyId)
            throw new BusinessRuleException(ErrorCodes.CheckoutItemCompanyMismatch, "Booking pripada drugoj tvrtki.");

        // Zaključaj ciljno sudjelovanje (isti lock kao svako drugo namirenje) prije provjere "već u otvorenom checkoutu".
        await _participationHandler.LockForUpdate(uow, organizationId, new[] { participation.Id.GetValueOrDefault() });
        // P2 (pregled 2E #4): nikad ne naplaćuj zastarjelu cijenu — PriceStale se uskladi pod lockom prije snapshota stavke.
        if (await _membershipCoverage.EnsurePriceCurrent(uow, organizationId, userId, participation.Id.GetValueOrDefault()))
            await uow.Context.Entry(participation).ReloadAsync();

        // P1 (D5): prihvatljivost otkazanog/izostalog sudjelovanja je FINANCIJSKA, ne po statusu — smije se namiriti samo uz
        // pozitivan preostali dug (aktivna naknada politike, iz jedine status-aware derivacije, svježe pod lockom).
        // Confirmed/Completed ostaju prihvatljivi kao i prije (predujam; preplatu i dalje odbija plaćanje).
        ParticipationSettlement settlement = ParticipationSettlement.Of(
            participation, await _checkoutHandler.GetItemsForParticipation(uow, organizationId, participation.Id.GetValueOrDefault()));
        bool isPolicyFee = !ParticipationOccupancy.Occupies(participation.Status);
        // P2 (2D): usluga pokrivena članarinom ili pokriće koje čeka evaluaciju se ne naplaćuje (bez članarine: no-op).
        SettlementExclusivityPolicy.EnsureNotMembershipCovered(participation);
        if (isPolicyFee && settlement.OutstandingAmount <= 0m)
            throw new BusinessRuleException(ErrorCodes.CheckoutItemNotEligible,
                "Otkazano/izostalo sudjelovanje nema naknadu za naplatu.");
        bool alreadyLocked = await uow.Context.CheckoutItems
            .AnyAsync(i => i.OrganizationId == organizationId && i.BookingSegmentParticipationId == participation.Id && i.LocksParticipation);
        if (alreadyLocked)
            throw new BusinessRuleException(
                ErrorCodes.BookingAlreadyInOpenCheckout, "Booking je već aktivna stavka u drugom otvorenom checkoutu.");

        DateTimeOffset now = _timeProvider.GetUtcNow();
        CheckoutItem item = new CheckoutItem
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            CheckoutId = checkoutId,
            Type = CheckoutItemType.Booking,
            // P1: otkazano/izostalo sudjelovanje se naplaćuje samo kao naknada politike (snapshot = trenutni dug).
            Description = isPolicyFee
                ? $"{execution.ServiceName ?? "Booking"} — naknada ({(participation.Status == ParticipationStatus.NoShow ? "izostanak" : "kasno otkazivanje")})"
                : execution.ServiceName ?? "Booking",
            UnitPrice = isPolicyFee ? settlement.MonetaryDue : participation.Amount,
            Quantity = 1,
            Amount = isPolicyFee ? settlement.MonetaryDue : participation.Amount,
            BookingSegmentParticipationId = participation.Id,
            LocksParticipation = true,
            SaleCommissionEmployeeId = await DefaultSaleCommissionEmployee(organizationId, userId),
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
            NewValue = $"Booking:{booking.Id}/Participation:{participation.Id}",
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

        DateTimeOffset now = _timeProvider.GetUtcNow();
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
            LocksParticipation = false,
            SaleCommissionEmployeeId = await DefaultSaleCommissionEmployee(organizationId, userId),
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

    /// <summary>P2 (2C, Q20/Q24) — stavka plaćanja zaduženja članarine. Zaduženje mora pripadati klijentu checkouta, biti
    /// otvoreno s preostalim dugom i ne smije biti stavka drugog otvorenog checkouta (lock zaduženja, isti mehanizam kao
    /// sudjelovanje). Iznos stavke = dio koji se sada plaća (djelomično dopušteno), najviše preostali dug.</summary>
    public async Task<CheckoutDto> AddMembershipChargeItem(Guid organizationId, Guid userId, Guid checkoutId, CheckoutAddMembershipChargeItemRequest request)
    {
        Guid chargeId = request?.MembershipChargeId ?? throw new ValidationAppException("MembershipChargeId je obavezan.");
        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

        Checkout locked = await LockOpenCheckout(uow, organizationId, checkoutId);
        MembershipCharge charge = await _membershipHandler.GetChargeForUpdate(uow, organizationId, chargeId)
            ?? throw new NotFoundAppException("MembershipCharge", chargeId);
        if (charge.ClientId != locked.ClientId)
            throw new BusinessRuleException(ErrorCodes.CheckoutItemClientMismatch, "Zaduženje članarine pripada drugom klijentu.");
        if (charge.CheckoutItems.Any(i => i.LocksMembershipCharge))
            throw new BusinessRuleException(ErrorCodes.MembershipChargeInOpenCheckout, "Zaduženje je već stavka otvorenog checkouta.");

        decimal outstanding = MembershipChargeSettlement.Outstanding(charge);
        if (outstanding <= 0m)
            throw new BusinessRuleException(ErrorCodes.MembershipChargeNotOpen, "Zaduženje nije otvoreno ili nema preostalog duga.");
        decimal amount = request.Amount ?? outstanding;
        if (amount <= 0m || amount > outstanding || decimal.Round(amount, 2) != amount)
            throw new ValidationAppException($"Iznos mora biti veći od 0, najviše {outstanding} (preostali dug), s najviše 2 decimale.");

        DateTimeOffset now = _timeProvider.GetUtcNow();
        CheckoutItem item = new CheckoutItem
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            CheckoutId = checkoutId,
            Type = CheckoutItemType.MembershipCharge,
            Description = charge.Description,
            UnitPrice = amount,
            Quantity = 1,
            Amount = amount,
            MembershipChargeId = charge.Id,
            LocksMembershipCharge = true,
            // 2F: zaduženje prve prodaje nosi korisnika provizije na članstvu (jedini izvor); obnova sprema odabir na stavci.
            SaleCommissionEmployeeId = MembershipFirstSale.IsFirstSaleCharge(charge, charge.Membership.StartsOn)
                ? null
                : await DefaultSaleCommissionEmployee(organizationId, userId),
            CreatedAt = now,
            CreatedBy = userId
        };

        try
        {
            await _checkoutHandler.AddItem(uow, item);
        }
        catch (DbUpdateException)
        {
            throw new BusinessRuleException(ErrorCodes.MembershipChargeInOpenCheckout, "Zaduženje je već stavka otvorenog checkouta.");
        }

        await _auditLogHandler.Add(uow, new CheckoutAuditLog
        {
            Id = Guid.NewGuid(),
            CheckoutId = checkoutId,
            ChangeType = "ItemAdded",
            NewValue = $"MembershipCharge:{charge.Id}:{amount}",
            ChangedAt = now,
            ChangedBy = userId
        });
        await uow.CommitAsync();

        return await GetById(organizationId, checkoutId);
    }

    /// <summary>P2 (Q16.3) — projekcija plaćenosti zaduženja članarine nakon promjene alokacija, u istoj transakciji.</summary>
    private async Task<Guid> ClientOf(Guid organizationId, Guid checkoutId)
    {
        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();
        return await uow.Context.Checkouts.Where(c => c.OrganizationId == organizationId && c.Id == checkoutId).Select(c => c.ClientId).SingleAsync();
    }

    /// <remarks>P2 (2D): plaćanje ili storno zaduženja mijenja stanje duga članstva (Q15) — pokriće budućih termina se
    /// usklađuje (plaćen dug vraća pokriće, storno nakon grace perioda ga oduzima). Storno uplate sudjelovanja vraća ga u
    /// evaluaciju (AlreadyPaid → pokriće ako limiti dopuštaju). Bez članarine no-op.</remarks>
    private async Task RefreshMembershipCharges(IUnitOfWork uow, Guid organizationId, Guid userId, Guid checkoutId, bool paymentVoided)
    {
        List<Guid> chargeIds = await uow.Context.CheckoutItems
            .Where(i => i.OrganizationId == organizationId && i.CheckoutId == checkoutId && i.MembershipChargeId != null)
            .Select(i => i.MembershipChargeId.Value)
            .Distinct()
            .ToListAsync();
        await _membershipHandler.RefreshChargeSettlement(uow, chargeIds);
        await uow.Context.SaveChangesAsync();

        if (chargeIds.Count > 0)
        {
            List<Guid> membershipIds = await uow.Context.MembershipCharges
                .Where(c => chargeIds.Contains(c.Id))
                .Select(c => c.ClientMembershipId)
                .Distinct()
                .OrderBy(id => id)
                .ToListAsync();
            foreach (Guid membershipId in membershipIds)
                await _membershipCoverage.ReconcileMembership(uow, organizationId, membershipId, MembershipCoverageEvent.DebtChanged, userId);
        }

        bool hasServiceItems = await uow.Context.CheckoutItems
            .AnyAsync(i => i.OrganizationId == organizationId && i.CheckoutId == checkoutId && i.BookingSegmentParticipationId != null);
        if (paymentVoided && hasServiceItems)
        {
            Guid clientId = await uow.Context.Checkouts.Where(c => c.Id == checkoutId).Select(c => c.ClientId).SingleAsync();
            await _membershipCoverage.ReconcileClient(uow, organizationId, clientId, MembershipCoverageEvent.PaymentChanged, userId);
        }
    }

    public async Task<CheckoutDto> AddProductItem(Guid organizationId, Guid userId, Guid checkoutId, CheckoutAddProductItemRequest request)
    {
        if (request.Quantity <= 0)
            throw new ValidationAppException(ErrorCodes.InvalidQuantity, "Količina mora biti veća od nule.");

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

        DateTimeOffset now = _timeProvider.GetUtcNow();

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
                LocksParticipation = false,
                SaleCommissionEmployeeId = await DefaultSaleCommissionEmployee(organizationId, userId),
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
            ChangedAt = _timeProvider.GetUtcNow(),
            ChangedBy = userId
        });
        await uow.CommitAsync();

        return await GetById(organizationId, checkoutId);
    }

    /// <summary>P2 (2F, §18.1/§16.3) — kome ide provizija na prodaju stavke, dok je checkout Open. Stavka zaduženja prve prodaje
    /// članarine mijenja korisnika na članstvu (jedini izvor; događaj u povijesti članstva; nakon nastanka provizije samo korekcija,
    /// Q50); ostale stavke spremaju odabir na stavci uz zapis u CheckoutAuditLog. Null = bez provizije na prodaju.</summary>
    public async Task<CheckoutDto> SetItemSaleCommissionEmployee(
        Guid organizationId, Guid userId, Guid checkoutId, Guid itemId, Core.DTOs.Commissions.SaleCommissionEmployeeRequest request)
    {
        Guid? employeeId = request?.EmployeeId;
        if (employeeId.HasValue)
            await _commissionLedgerService.EnsureSelectableEmployee(organizationId, employeeId.Value);

        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();
        await LockOpenCheckout(uow, organizationId, checkoutId);

        CheckoutItem item = await uow.Context.CheckoutItems
            .Include(i => i.MembershipCharge).ThenInclude(c => c.Membership)
            .SingleOrDefaultAsync(i => i.OrganizationId == organizationId && i.CheckoutId == checkoutId && i.Id == itemId)
            ?? throw new NotFoundAppException("CheckoutItem", itemId);

        if (SaleCommissionFromMembership(item))
        {
            await _commissionLedgerService.SetMembershipSaleCommissionEmployee(
                uow, organizationId, userId, item.MembershipCharge.ClientMembershipId, employeeId, $"Checkout:{checkoutId}");
        }
        else if (item.SaleCommissionEmployeeId != employeeId)
        {
            Guid? previous = item.SaleCommissionEmployeeId;
            item.SaleCommissionEmployeeId = employeeId;
            await uow.Context.SaveChangesAsync();
            await _auditLogHandler.Add(uow, new CheckoutAuditLog
            {
                Id = Guid.NewGuid(),
                CheckoutId = checkoutId,
                ChangeType = "SaleCommissionEmployeeChanged",
                OldValue = $"{item.Id}:{previous}",
                NewValue = $"{item.Id}:{employeeId}",
                ChangedAt = _timeProvider.GetUtcNow(),
                ChangedBy = userId
            });
        }

        await uow.CommitAsync();
        return await GetById(organizationId, checkoutId);
    }

    public async Task<CheckoutDto> RecordPayment(Guid organizationId, Guid userId, Guid checkoutId, CheckoutPaymentCreateRequest request)
    {
        if (request.Amount <= 0m)
            throw new ValidationAppException(ErrorCodes.InvalidQuantity, "Iznos plaćanja mora biti veći od 0.");

        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

        await LockOpenCheckout(uow, organizationId, checkoutId);
        // Phase D3B3B: zaključaj sudjelovanja stavki usluge PRIJE učitavanja grafa — svako novčano namirenje istog
        // sudjelovanja (ovaj ili drugi checkout, check-in plaćanje) se serijalizira, pa izračun duga ispod vidi svježe stanje.
        List<Guid> serviceParticipationIds = await _checkoutHandler.GetServiceParticipationIds(uow, organizationId, checkoutId);
        await _participationHandler.LockForUpdate(uow, organizationId, serviceParticipationIds);
        // P2 (pregled 2E #4): zastarjela cijena sudjelovanja se uskladi prije izračuna duga (bez članarine no-op).
        foreach (Guid participationId in serviceParticipationIds)
            await _membershipCoverage.EnsurePriceCurrent(uow, organizationId, userId, participationId);
        Checkout graph = await _checkoutHandler.GetGraph(uow, organizationId, checkoutId);

        List<CheckoutItem> orderedItems = graph.Items.OrderBy(i => i.CreatedAt).ToList();
        List<CheckoutItemFinancials> financials = orderedItems.Select(CheckoutFinancialsCalculator.CalculateItem).ToList();
        decimal totalOutstanding = financials.Sum(f => f.OutstandingAmount);

        if (request.Amount > totalOutstanding)
            throw new BusinessRuleException(
                ErrorCodes.PaymentExceedsOutstandingAmount, "Iznos premašuje preostali dug checkouta.",
                new { outstanding = totalOutstanding, requested = request.Amount });

        DateTimeOffset now = _timeProvider.GetUtcNow();
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
        await RefreshMembershipCharges(uow, organizationId, userId, checkoutId, paymentVoided: false);
        await SyncPolicyFeeCommissions(uow, organizationId, userId, checkoutId);

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

        // P2 (Q53): plaćeno zaduženje može okončati dug — preskočeni budući termini grupe se popunjavaju (vlastite transakcije).
        await _membershipSkips.BackfillForClient(organizationId, await ClientOf(organizationId, checkoutId), userId);
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
            throw new ValidationAppException(ErrorCodes.InvalidQuantity, "Svaka alokacija mora biti veća od 0.");

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

            // Eksplicitna provjera (uz već-ispravan MonetaryDue=0 izračun ispod) da alokacija ne smije ciljati stavku
            // usluge čije je sudjelovanje trenutno pokriveno paketom — jedino pravilo SettlementExclusivityPolicy.
            CheckoutItem targetItem = orderedItems[index];
            if (targetItem.Type == CheckoutItemType.Booking && targetItem.Participation != null)
                SettlementExclusivityPolicy.EnsureMoneyAllowed(targetItem.Participation);

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
        // Ručno (administrativno) poništenje plaćanja mora imati razlog — za razliku od sustavskog
        // VoidCheckInGeneratedPayments (deterministički razlog, ne prolazi kroz ovaj request DTO), ovdje je
        // uvijek stvarna osoba koja svjesno poništava naplatu (vidi audit-cleanup spec section 6).
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationAppException(ErrorCodes.PaymentVoidReasonRequired, "Razlog poništenja plaćanja je obavezan.");

        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

        await LockOpenCheckout(uow, organizationId, checkoutId);

        Payment payment = await _checkoutHandler.GetPayment(uow, organizationId, checkoutId, paymentId);
        if (payment == null)
            throw new NotFoundAppException("Payment", paymentId);
        if (payment.Status == PaymentStatus.Voided)
            throw new BusinessRuleException(ErrorCodes.PaymentAlreadyVoided, "Plaćanje je već poništeno.");

        payment.Status = PaymentStatus.Voided;
        payment.VoidedAt = _timeProvider.GetUtcNow();
        payment.VoidedBy = userId;
        payment.VoidReason = request.Reason.Trim();

        await _checkoutHandler.UpdatePayment(uow, payment);
        await RefreshMembershipCharges(uow, organizationId, userId, checkoutId, paymentVoided: true);
        await SyncPolicyFeeCommissions(uow, organizationId, userId, checkoutId);

        await _auditLogHandler.Add(uow, new CheckoutAuditLog
        {
            Id = Guid.NewGuid(),
            CheckoutId = checkoutId,
            ChangeType = "PaymentVoided",
            OldValue = $"{payment.Amount} {payment.Method}",
            NewValue = "Voided",
            ChangedAt = _timeProvider.GetUtcNow(),
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

        DateTimeOffset now = _timeProvider.GetUtcNow();

        List<CheckoutItem> productItems = graph.Items.Where(i => i.Type == CheckoutItemType.Product).ToList();
        await _stockLedgerService.ConsumeForSale(uow, organizationId, userId, graph.CompanyId, productItems, now);

        foreach (CheckoutItem item in graph.Items.Where(i => i.Type == CheckoutItemType.Package && i.ClientPackageId == null))
            await IssueClientPackage(uow, organizationId, userId, graph, item, now);

        graph.Status = CheckoutStatus.Completed;
        graph.CompletedAt = now;
        graph.CompletedBy = userId;
        foreach (CheckoutItem item in graph.Items.Where(i => i.LocksParticipation))
            item.LocksParticipation = false;
        foreach (CheckoutItem item in graph.Items.Where(i => i.LocksMembershipCharge))
            item.LocksMembershipCharge = false;

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

        DateTimeOffset now = _timeProvider.GetUtcNow();
        graph.Status = CheckoutStatus.Cancelled;
        graph.CancelledAt = now;
        graph.CancelledBy = userId;
        foreach (CheckoutItem item in graph.Items.Where(i => i.LocksParticipation))
            item.LocksParticipation = false;
        foreach (CheckoutItem item in graph.Items.Where(i => i.LocksMembershipCharge))
            item.LocksMembershipCharge = false;

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
            CompanyId = companyId
            // T1-7: Date null = danas po poslovnom satu u zoni poslovnice checkouta.
        });
        return resolved.Price;
    }

    private async Task IssueClientPackage(
        IUnitOfWork uow, Guid organizationId, Guid userId, Checkout checkout, CheckoutItem item, DateTimeOffset purchasedAt)
    {
        Package package = await _packageHandler.GetById(organizationId, item.PackageId.GetValueOrDefault());
        if (package == null)
            throw new NotFoundAppException("Package", item.PackageId.GetValueOrDefault());
        if (!package.IsActive)
            throw new BusinessRuleException(
                ErrorCodes.InactivePackage, $"Paket '{package.Name}' više nije aktivan — kupnja se ne može dovršiti.");

        // T1-7: PurchaseDate = poslovni dan kupnje = lokalni datum trenutka checkouta u zoni poslovnice checkouta; trenutak
        // prodaje ostaje CreatedAt (i na checkoutu/plaćanju).
        DateOnly purchaseDay = (await _organizationCalendarService.GetCompanyCalendar(organizationId, checkout.CompanyId)).LocalDate(purchasedAt);
        DateOnly validUntilDate = PackageExpiryCalculator.ForSale(package, purchaseDay);

        Guid clientPackageId = Guid.NewGuid();
        ClientPackage clientPackage = new ClientPackage
        {
            Id = clientPackageId,
            OrganizationId = organizationId,
            ClientId = checkout.ClientId,
            PackageId = package.Id.GetValueOrDefault(),
            PurchaseDate = purchaseDay,
            // Cijena je već snapshotana na CheckoutItem.Amount kod dodavanja — NE re-razrješava se ovdje (vidi
            // spec section 25).
            PaidPrice = item.Amount,
            EntryMode = package.EntryMode,
            TotalEntryCount = package.TotalEntryCount,
            RemainingSharedEntries = package.EntryMode == PackageEntryMode.SharedPool ? package.TotalEntryCount : null,
            ValidityType = package.ValidityType,
            ValidUntilDate = validUntilDate,
            Status = ClientPackageStatus.Active,
            CreatedAt = purchasedAt,
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
            ChangedAt = purchasedAt,
            ChangedBy = userId
        });
    }

    /// <summary>Pregled 2E — otvoreni checkout: stavka AKTIVNE sesije čiji iznos (snapshot pri dodavanju) se razlikuje od trenutnog
    /// duga sesije (automatska promjena cijene zbog članarine ili ručna promjena cijene) daje upozorenje s oba iznosa. Stavka se ne
    /// mijenja automatski; recepcija je osvježava uklanjanjem i ponovnim dodavanjem prije zatvaranja.</summary>
    private static List<WarningDto> ItemPriceWarnings(Checkout checkout, List<CheckoutItem> orderedItems)
    {
        if (checkout.Status != CheckoutStatus.Open)
            return new List<WarningDto>();

        List<WarningCheckoutItemPrice> changed = orderedItems
            .Where(i => i.Type == CheckoutItemType.Booking && i.Participation != null && ParticipationOccupancy.Occupies(i.Participation.Status))
            .Select(i => new WarningCheckoutItemPrice
            {
                CheckoutItemId = i.Id.GetValueOrDefault(),
                ParticipationId = i.BookingSegmentParticipationId.GetValueOrDefault(),
                ItemAmount = i.Amount,
                CurrentDue = ParticipationSettlement.Of(i.Participation).MonetaryDue
            })
            .Where(x => x.ItemAmount != x.CurrentDue)
            .ToList();
        return changed.Count == 0
            ? new List<WarningDto>()
            : new List<WarningDto> { new(WarningCodes.CheckoutItemPriceChanged, new WarningCheckoutItemPriceDetails { Items = changed }) };
    }

    /// <summary>P2 (2F, §18.1) — default korisnik provizije na prodaju nove stavke: aktivan zaposlenik korisnika koji radi s
    /// checkoutom (User → Employee); bez njega null (nema provizije dok se ne odabere).</summary>
    private async Task<Guid?> DefaultSaleCommissionEmployee(Guid organizationId, Guid userId)
    {
        Employee employee = await _employeeHandler.GetByUserId(organizationId, userId);
        return employee is { IsActive: true } ? employee.Id : null;
    }

    /// <summary>P2 (2F) — stavka zaduženja PRVE prodaje članarine: korisnik provizije je na članstvu (jedini izvor).</summary>
    private static bool SaleCommissionFromMembership(CheckoutItem item) =>
        item.MembershipCharge?.Membership != null
        && MembershipFirstSale.IsFirstSaleCharge(item.MembershipCharge, item.MembershipCharge.Membership.StartsOn);

    /// <summary>P2 (2F, Q38) — promjena uplata mijenja plaćenost P1 naknada otkazanih/izostalih sudjelovanja checkouta; provizija
    /// na naknadu se usklađuje u istoj transakciji (bez naknade no-op).</summary>
    private async Task SyncPolicyFeeCommissions(IUnitOfWork uow, Guid organizationId, Guid userId, Guid checkoutId)
    {
        List<Guid> participationIds = await uow.Context.CheckoutItems
            .Where(i => i.OrganizationId == organizationId && i.CheckoutId == checkoutId && i.BookingSegmentParticipationId != null &&
                        (i.Participation.Status == ParticipationStatus.Cancelled || i.Participation.Status == ParticipationStatus.NoShow))
            .Select(i => i.BookingSegmentParticipationId.Value)
            .Distinct()
            .ToListAsync();
        foreach (Guid participationId in participationIds.OrderBy(id => id))
            await _commissionLedgerService.SyncPolicyFeeCommission(uow, organizationId, userId, participationId);
    }

    private static CheckoutDto ToDto(Checkout checkout)
    {
        List<CheckoutItem> orderedItems = checkout.Items.OrderBy(i => i.CreatedAt).ToList();
        CheckoutFinancials totals = CheckoutFinancialsCalculator.Calculate(checkout);

        List<CheckoutItemDto> items = orderedItems.Select(item =>
        {
            CheckoutItemFinancials financials = CheckoutFinancialsCalculator.CalculateItem(item);
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
                // Stavka usluge prikazuje sudjelovanje i njegov Booking (spremnik).
                BookingId = item.Participation?.BookingId,
                ParticipationId = item.BookingSegmentParticipationId,
                PackageId = item.PackageId,
                ProductId = item.ProductId,
                ClientPackageId = item.ClientPackageId,
                MembershipChargeId = item.MembershipChargeId,
                SaleCommissionEmployeeId = SaleCommissionFromMembership(item) ? item.MembershipCharge.Membership.SaleCommissionEmployeeId : item.SaleCommissionEmployeeId,
                SaleCommissionFromMembership = SaleCommissionFromMembership(item),
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
            Warnings = ItemPriceWarnings(checkout, orderedItems),
            CreatedAt = checkout.CreatedAt,
            CreatedBy = checkout.CreatedBy,
            CompletedAt = checkout.CompletedAt,
            CompletedBy = checkout.CompletedBy,
            CancelledAt = checkout.CancelledAt,
            CancelledBy = checkout.CancelledBy
        };
    }
}
