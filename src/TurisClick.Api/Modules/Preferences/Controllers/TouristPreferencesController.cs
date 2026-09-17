using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Preferences.Dtos;
using TurisClick.Api.Modules.Preferences.Services;

namespace TurisClick.Api.Modules.Preferences.Controllers;

/// <summary>Onboarding y perfil de viaje del turista. Siempre del usuario autenticado: no hay id en la ruta.</summary>
[ApiController]
[Route("api/tourists/me/preferences")]
[Authorize(Policy = "RequireTourist")]
public class TouristPreferencesController(ITouristPreferenceService preferenceService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TouristPreferencesResponse>> Get(CancellationToken ct) =>
        Ok(await preferenceService.GetMineAsync(ct));

    [HttpPut]
    public async Task<ActionResult<TouristPreferencesResponse>> Update([FromBody] UpdateTouristPreferencesRequest request, CancellationToken ct) =>
        Ok(await preferenceService.UpdateMineAsync(request, ct));
}
