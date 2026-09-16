using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Checkouts;
using BlueDragon.DuneLight.Core.Interfaces.Checkouts;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Checkouts;

/// <summary>POS/Checkout temelji — vidi ICheckoutService za domensku napomenu.</summary>
[ApiController]
[Route("api/checkouts")]
[Produces("application/json")]
public class CheckoutsController : ControllerBase
{
    private readonly ICheckoutService _checkoutService;

    public CheckoutsController(ICheckoutService checkoutService)
    {
        _checkoutService = checkoutService;
    }

    /// <summary>Povijest Checkouta jednog klijenta, najnoviji prvi — za Povijest klijenta.</summary>
    [HttpGet]
    [RequireGrant(Grants.CheckoutView)]
    public async Task<ActionResult<List<CheckoutDto>>> GetByClient([FromQuery] Guid clientId)
    {
        return Ok(await _checkoutService.GetByClient(this.CurrentOrganizationId(), clientId));
    }

    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.CheckoutView)]
    public async Task<ActionResult<CheckoutDto>> GetById(Guid id)
    {
        return Ok(await _checkoutService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [RequireGrant(Grants.CheckoutManage)]
    public async Task<ActionResult<CheckoutDto>> Create([FromBody] CheckoutCreateRequest request)
    {
        CheckoutDto created = await _checkoutService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Dodaje postojeći Booking kao stavku — server snapshotta cijenu/opis, ne prima iznos.</summary>
    [HttpPost("{id:guid}/items/booking")]
    [RequireGrant(Grants.CheckoutManage)]
    public async Task<ActionResult<CheckoutDto>> AddBookingItem(Guid id, [FromBody] CheckoutAddBookingItemRequest request)
    {
        return Ok(await _checkoutService.AddBookingItem(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    /// <summary>Dodaje kupnju Paketa kao stavku — cijena se razrješava preko cjenika, ne prima iznos. Ne izdaje
    /// ClientPackage odmah (tek na complete).</summary>
    [HttpPost("{id:guid}/items/package")]
    [RequireGrant(Grants.CheckoutManage)]
    public async Task<ActionResult<CheckoutDto>> AddPackageItem(Guid id, [FromBody] CheckoutAddPackageItemRequest request)
    {
        return Ok(await _checkoutService.AddPackageItem(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    /// <summary>Dodaje Product stavku — server razrješava UnitPrice iz Product.DefaultPrice, ne prima iznos.
    /// Ponovno dodavanje istog Producta povećava Quantity postojeće stavke umjesto duplikata. Ne dira zalihu
    /// (konzumacija se događa tek na Complete).</summary>
    [HttpPost("{id:guid}/items/product")]
    [RequireGrant(Grants.CheckoutManage)]
    public async Task<ActionResult<CheckoutDto>> AddProductItem(Guid id, [FromBody] CheckoutAddProductItemRequest request)
    {
        return Ok(await _checkoutService.AddProductItem(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    /// <summary>Uklanja stavku iz Open checkouta — blokirano ako stavka ima aktivnu novčanu alokaciju.</summary>
    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    [RequireGrant(Grants.CheckoutManage)]
    public async Task<ActionResult<CheckoutDto>> RemoveItem(Guid id, Guid itemId)
    {
        return Ok(await _checkoutService.RemoveItem(this.CurrentOrganizationId(), this.CurrentUserId(), id, itemId));
    }

    /// <summary>Bilježi novčanu naplatu — automatska FIFO raspodjela po stavkama ako Allocations izostavljen.</summary>
    [HttpPost("{id:guid}/payments")]
    [RequireGrant(Grants.CheckoutManage)]
    public async Task<ActionResult<CheckoutDto>> RecordPayment(Guid id, [FromBody] CheckoutPaymentCreateRequest request)
    {
        return Ok(await _checkoutService.RecordPayment(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    /// <summary>Poništava Payment — dopušteno samo dok je Checkout Open.</summary>
    [HttpPost("{id:guid}/payments/{paymentId:guid}/void")]
    [RequireGrant(Grants.CheckoutManage)]
    public async Task<ActionResult<CheckoutDto>> VoidPayment(Guid id, Guid paymentId, [FromBody] CheckoutPaymentVoidRequest request)
    {
        return Ok(await _checkoutService.VoidPayment(this.CurrentOrganizationId(), this.CurrentUserId(), id, paymentId, request));
    }

    /// <summary>Zatvara Checkout — zahtijeva potpuno namiren MonetaryDue, atomarno izdaje ClientPackage za
    /// Package stavke.</summary>
    [HttpPost("{id:guid}/complete")]
    [RequireGrant(Grants.CheckoutManage)]
    public async Task<ActionResult<CheckoutDto>> Complete(Guid id)
    {
        return Ok(await _checkoutService.Complete(this.CurrentOrganizationId(), this.CurrentUserId(), id));
    }

    /// <summary>Otkazuje Open checkout — dopušteno samo bez aktivnih plaćanja.</summary>
    [HttpPost("{id:guid}/cancel")]
    [RequireGrant(Grants.CheckoutManage)]
    public async Task<ActionResult<CheckoutDto>> Cancel(Guid id)
    {
        return Ok(await _checkoutService.Cancel(this.CurrentOrganizationId(), this.CurrentUserId(), id));
    }
}
