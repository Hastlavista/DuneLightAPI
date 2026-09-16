using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Products;
using BlueDragon.DuneLight.Core.Interfaces.Products;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Products;

/// <summary>Zaliha proizvoda po poslovnici — vidi IStockService za domensku napomenu.</summary>
[ApiController]
[Route("api/stock")]
[Produces("application/json")]
public class StockController : ControllerBase
{
    private readonly IStockService _stockService;

    public StockController(IStockService stockService)
    {
        _stockService = stockService;
    }

    [HttpGet("company/{companyId:guid}")]
    [RequireGrant(Grants.StockView)]
    public async Task<ActionResult<List<ProductStockDto>>> GetByCompany(Guid companyId)
    {
        return Ok(await _stockService.GetByCompany(this.CurrentOrganizationId(), companyId));
    }

    [HttpGet("product/{productId:guid}")]
    [RequireGrant(Grants.StockView)]
    public async Task<ActionResult<List<ProductStockDto>>> GetByProduct(Guid productId)
    {
        return Ok(await _stockService.GetByProduct(this.CurrentOrganizationId(), productId));
    }

    [HttpGet("product/{productId:guid}/movements")]
    [RequireGrant(Grants.StockView)]
    public async Task<ActionResult<List<StockMovementDto>>> GetMovements(Guid productId)
    {
        return Ok(await _stockService.GetMovements(this.CurrentOrganizationId(), productId));
    }

    /// <summary>Postavlja zalihu na apsolutnu vrijednost — server izračunava deltu (vidi StockAdjustRequest).</summary>
    [HttpPost("product/{productId:guid}/company/{companyId:guid}/adjust")]
    [RequireGrant(Grants.StockManage)]
    public async Task<ActionResult<ProductStockDto>> Adjust(Guid productId, Guid companyId, [FromBody] StockAdjustRequest request)
    {
        return Ok(await _stockService.Adjust(this.CurrentOrganizationId(), this.CurrentUserId(), productId, companyId, request));
    }

    [HttpPost("transfers")]
    [RequireGrant(Grants.StockManage)]
    public async Task<ActionResult<StockTransferResultDto>> Transfer([FromBody] StockTransferRequest request)
    {
        return Ok(await _stockService.Transfer(this.CurrentOrganizationId(), this.CurrentUserId(), request));
    }
}
