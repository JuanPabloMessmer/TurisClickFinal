using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Ai.Dtos;
using TurisClick.Api.Modules.Ai.Services;

namespace TurisClick.Api.Modules.Ai.Controllers;

/// <summary>Getter de soporte por id — 403 si el itinerario no pertenece al TOURIST autenticado. "Mis itinerarios" (UC-T-17, guardados) es Oleada 6.</summary>
[ApiController]
[Route("api/ai/itineraries")]
[Authorize(Policy = "RequireTourist")]
public class AiItinerariesController(IAiConversationService conversationService) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ItineraryResponse>> GetById(Guid id, CancellationToken ct)
    {
        var result = await conversationService.GetItineraryByIdAsync(id, ct);
        return Ok(result);
    }
}
