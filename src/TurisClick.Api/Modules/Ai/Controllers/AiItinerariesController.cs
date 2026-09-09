using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Ai.Dtos;
using TurisClick.Api.Modules.Ai.Services;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Ai.Controllers;

/// <summary>
/// UC-T-16/17 y UC-AI-06 — exclusivo TOURIST, ownership estricto (403 si el itinerario es de otro).
/// Iterar un itinerario (UC-T-15) NO pasa por acá: usa el endpoint de mensajes de la conversación.
/// Reservar (UC-T-18) es Oleada 7.
/// </summary>
[ApiController]
[Route("api/ai/itineraries")]
[Authorize(Policy = "RequireTourist")]
public class AiItinerariesController(IAiItineraryService itineraryService) : ControllerBase
{
    /// <summary>UC-T-17 — "Mis itinerarios guardados".</summary>
    [HttpGet("me")]
    public async Task<ActionResult<PagedResult<SavedItinerarySummaryResponse>>> ListSavedMine(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await itineraryService.ListSavedMineAsync(page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>UC-T-14/17 — detalle revalidado contra el catálogo vigente (precios/cupos pueden haber cambiado).</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ItineraryResponse>> GetById(Guid id, CancellationToken ct)
    {
        var result = await itineraryService.GetByIdAsync(id, ct);
        return Ok(result);
    }

    /// <summary>UC-T-16 — guardar NO reserva ni retiene cupos; solo marca la propuesta como SAVED.</summary>
    [HttpPost("{id:guid}/save")]
    public async Task<ActionResult<ItineraryResponse>> Save(Guid id, CancellationToken ct)
    {
        var result = await itineraryService.SaveAsync(id, ct);
        return Ok(result);
    }

    /// <summary>UC-AI-06 — por qué este componente está en el itinerario. No modifica nada.</summary>
    [HttpGet("{id:guid}/items/{itemId:guid}/explanation")]
    public async Task<ActionResult<ItemExplanationResponse>> GetItemExplanation(Guid id, Guid itemId, CancellationToken ct)
    {
        var result = await itineraryService.GetItemExplanationAsync(id, itemId, ct);
        return Ok(result);
    }
}
