using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Categories.Dtos;
using TurisClick.Api.Modules.Categories.Services;

namespace TurisClick.Api.Modules.Categories.Controllers;

/// <summary>
/// Lectura pública del catálogo maestro de categorías, distinta de /api/admin/categories (gestión,
/// UC-A-05). Existe porque UC-T-04/06 aceptan `categoryId` como filtro de búsqueda: sin este endpoint
/// un cliente anónimo puede filtrar por categoría pero no descubrir cuáles hay.
///
/// Solo lectura y solo la lista: crear/editar/borrar sigue siendo exclusivo de ADMIN.
/// </summary>
[ApiController]
[Route("api/categories")]
[AllowAnonymous]
public class PublicCategoriesController(ICategoryService categoryService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CategoryResponse>>> List(CancellationToken ct)
    {
        // Mismo DTO que la vista de administración: `CategoryResponse` ya expone únicamente
        // Id/Name/Description, que es exactamente lo que el catálogo público necesita para pintar y
        // filtrar. No hay datos internos que recortar.
        var result = await categoryService.ListAsync(ct);
        return Ok(result);
    }
}
