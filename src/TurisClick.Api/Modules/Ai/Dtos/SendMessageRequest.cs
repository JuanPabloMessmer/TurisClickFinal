using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Ai.Dtos;

/// <summary>UC-T-13 — Enviar preferencias de viaje a la IA (un mensaje en lenguaje natural).</summary>
public class SendMessageRequest
{
    [Required, MinLength(1), MaxLength(2000)]
    public string Content { get; set; } = string.Empty;
}
