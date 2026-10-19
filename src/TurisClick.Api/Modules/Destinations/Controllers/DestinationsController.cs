using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Destinations.Entities;
using TurisClick.Api.Modules.Destinations.Services;

namespace TurisClick.Api.Modules.Destinations.Controllers;

/// <summary>UC-A-04 — Gestionar destinos. Catálogo maestro, exclusivo de ADMIN.</summary>
[ApiController]
[Route("api/admin/destinations")]
[Authorize(Policy = "RequireAdmin")]
public class DestinationsController(
    IDestinationService destinationService,
    IDestinationImageService destinationImageService) : ControllerBase
{
    /// <summary>
    /// Tope del cuerpo multipart. Holgado a propósito sobre DestinationImageService.MaxBytes (5 MB): una imagen
    /// de 6 u 8 MB llega al Service y recibe el mensaje claro en español, en vez del 413 genérico de Kestrel.
    /// </summary>
    private const long MaxUploadRequestBytes = 10 * 1024 * 1024;

    [HttpGet]
    public async Task<ActionResult<List<DestinationResponse>>> List(
        [FromQuery] Guid? parentId, [FromQuery] DestinationType? type, CancellationToken ct)
    {
        var result = await destinationService.ListAsync(parentId, type, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DestinationResponse>> GetById(Guid id, CancellationToken ct)
    {
        var result = await destinationService.GetByIdAsync(id, ct);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(DestinationResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<DestinationResponse>> Create([FromBody] CreateDestinationRequest request, CancellationToken ct)
    {
        var result = await destinationService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DestinationResponse>> Update(Guid id, [FromBody] UpdateDestinationRequest request, CancellationToken ct)
    {
        var result = await destinationService.UpdateAsync(id, request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Sube la imagen representativa de un destino (JPG, PNG o WebP, hasta 5 MB) y devuelve su URL pública
    /// HTTPS. No toca ningún destino: el Backoffice manda esa URL como ImageUrl en el POST/PUT del destino.
    /// </summary>
    [HttpPost("images")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    [ProducesResponseType(typeof(UploadedImageResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<UploadedImageResponse>> UploadImage(IFormFile? file, CancellationToken ct)
    {
        var result = await destinationImageService.UploadAsync(file, ct);
        return Created(result.Url, result);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await destinationService.DeleteAsync(id, ct);
        return NoContent();
    }
}
