using System.ComponentModel.DataAnnotations;

namespace TurisClick.Api.Modules.Packages.Dtos;

/// <summary>
/// Alta masiva de salidas por calendario: rango + patrón semanal + cupo. Las salidas de un paquete no
/// tienen horario (una por fecha), así que no hay StartTimes.
/// </summary>
public class BulkCreatePackageAvailabilityRequest
{
    [Required]
    public DateOnly StartDate { get; set; }

    [Required]
    public DateOnly EndDate { get; set; }

    /// <summary>EVERY_DAY, WEEKDAYS, WEEKENDS o CUSTOM (default).</summary>
    public string? Preset { get; set; }

    /// <summary>Solo CUSTOM: 0 = domingo … 6 = sábado.</summary>
    public List<int> Weekdays { get; set; } = [];

    [Range(1, 10_000)]
    public int TotalSlots { get; set; }

    public bool DryRun { get; set; }
}

public class BulkPackageAvailabilityResponse
{
    public int RequestedCount { get; set; }
    public int CreatedCount { get; set; }
    public int SkippedCount { get; set; }
    public bool DryRun { get; set; }
    public List<PackageAvailabilityResponse> Created { get; set; } = [];
    public List<SkippedDepartureResponse> Skipped { get; set; } = [];
}

public class SkippedDepartureResponse
{
    public DateOnly DepartureDate { get; set; }

    /// <summary>ALREADY_EXISTS: la salida ya existía y NO se modificó.</summary>
    public string Reason { get; set; } = string.Empty;
}
