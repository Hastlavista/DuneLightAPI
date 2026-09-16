using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Products;
using BlueDragon.DuneLight.Core.Interfaces.Products;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Products;

/// <summary>Katalog maloprodajnih proizvoda — vidi IProductService za domensku napomenu.</summary>
[ApiController]
[Route("api/products")]
[Produces("application/json")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpGet]
    [RequireGrant(Grants.ProductsView)]
    public async Task<ActionResult<PagedResult<ProductDto>>> GetPaged([FromQuery] PagedRequest request)
    {
        return Ok(await _productService.GetPaged(this.CurrentOrganizationId(), request));
    }

    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.ProductsView)]
    public async Task<ActionResult<ProductDto>> GetById(Guid id)
    {
        return Ok(await _productService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [RequireGrant(Grants.ProductsManage)]
    public async Task<ActionResult<ProductDto>> Create([FromBody] ProductCreateRequest request)
    {
        ProductDto created = await _productService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequireGrant(Grants.ProductsManage)]
    public async Task<ActionResult<ProductDto>> Update(Guid id, [FromBody] ProductUpdateRequest request)
    {
        return Ok(await _productService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPatch("{id:guid}/activate")]
    [RequireGrant(Grants.ProductsManage)]
    public async Task<ActionResult<ProductDto>> Activate(Guid id)
    {
        return Ok(await _productService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, true));
    }

    [HttpPatch("{id:guid}/deactivate")]
    [RequireGrant(Grants.ProductsManage)]
    public async Task<ActionResult<ProductDto>> Deactivate(Guid id)
    {
        return Ok(await _productService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, false));
    }

    [HttpDelete("{id:guid}")]
    [RequireGrant(Grants.ProductsManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _productService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }
}
