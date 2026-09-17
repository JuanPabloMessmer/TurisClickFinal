using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Experiences.Dtos;

/// <summary>
/// Alta masiva por calendario: rango + patrón semanal + horarios + cupo. El backend expande el patrón a
/// fechas concretas (ver Shared/Scheduling/AvailabilitySchedule); no se guarda ninguna regla recurrente.
/// </summary>
public class BulkCreateExperienceAvailabilityRequest
{
    [Required]
    public DateOnly StartDate { get; set; }

    [Required]
    public DateOnly EndDate { get; set; }

    /// <summary>EVERY_DAY, WEEKDAYS, WEEKENDS o CUSTOM (default). Los presets ignoran <see cref="Weekdays"/>.</summary>
    public string? Preset { get; set; }

    /// <summary>Solo CUSTOM: 0 = domingo … 6 = sábado.</summary>
    public List<int> Weekdays { get; set; } = [];

    /// <summary>Uno o más horarios por fecha. Vacío = disponibilidad de día completo.</summary>
    public List<TimeOnly> StartTimes { get; set; } = [];

    [Range(1, 10_000)]
    public int TotalSlots { get; set; }

    /// <summary>true = sólo calcula qué se crearía y qué se omitiría, sin escribir nada (vista previa del Backoffice).</summary>
    public bool DryRun { get; set; }
}

public class BulkExperienceAvailabilityResponse
{
    public int RequestedCount { get; set; }
    public int CreatedCount { get; set; }
    public int SkippedCount { get; set; }
    public bool DryRun { get; set; }

    /// <summary>En DryRun, las que se crearían (sin Id real).</summary>
    public List<ExperienceAvailabilityResponse> Created { get; set; } = [];
    public List<SkippedAvailabilityResponse> Skipped { get; set; } = [];
}

public class SkippedAvailabilityResponse
{
    public DateOnly Date { get; set; }
    public TimeOnly? StartTime { get; set; }

    /// <summary>ALREADY_EXISTS: esa fecha/horario ya existía y NO se modificó (conserva su cupo y sus reservas).</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Ajuste de una fecha puntual. Cambiar el cupo nunca puede dejarlo por debajo de lo ya reservado, y cerrar
/// una fecha sólo impide reservas NUEVAS: las existentes se respetan.
/// </summary>
public class UpdateAvailabilityRequest
{
    [Range(1, 10_000)]
    public int? TotalSlots { get; set; }

    /// <summary>OPEN o CLOSED.</summary>
    public string? Status { get; set; }
}
