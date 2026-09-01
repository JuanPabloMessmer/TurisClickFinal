using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Ai.Dtos;
using TurisClick.Api.Modules.Ai.Services;
using TurisClick.Api.Shared.Responses;

namespace TurisClick.Api.Modules.Ai.Controllers;

/// <summary>UC-T-12/13/14 — exclusivo TOURIST. Ver docs/use-cases.md; UC-T-15..18 (iterar/guardar/reservar) son oleadas futuras.</summary>
[ApiController]
[Route("api/ai/conversations")]
[Authorize(Policy = "RequireTourist")]
public class AiConversationsController(IAiConversationService conversationService) : ControllerBase
{
    /// <summary>UC-T-12.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ConversationResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<ConversationResponse>> Create(CancellationToken ct)
    {
        var result = await conversationService.CreateAsync(ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>"Mis conversaciones" — necesario para poder continuarlas (UC-T-13).</summary>
    [HttpGet("me")]
    public async Task<ActionResult<PagedResult<ConversationSummaryResponse>>> ListMine(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await conversationService.ListMineAsync(page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ConversationResponse>> GetById(Guid id, CancellationToken ct)
    {
        var result = await conversationService.GetByIdAsync(id, ct);
        return Ok(result);
    }

    /// <summary>UC-T-13 — dispara UC-AI-01 (extracción) y, si hay suficiente información, UC-AI-02/03/04 (propuesta).</summary>
    [HttpPost("{id:guid}/messages")]
    public async Task<ActionResult<SendMessageResponse>> SendMessage(Guid id, [FromBody] SendMessageRequest request, CancellationToken ct)
    {
        var result = await conversationService.SendMessageAsync(id, request, ct);
        return Ok(result);
    }

    /// <summary>UC-T-14 — la propuesta vigente de esta conversación.</summary>
    [HttpGet("{id:guid}/itinerary")]
    public async Task<ActionResult<ItineraryResponse>> GetLatestItinerary(Guid id, CancellationToken ct)
    {
        var result = await conversationService.GetLatestItineraryAsync(id, ct);
        return Ok(result);
    }
}
