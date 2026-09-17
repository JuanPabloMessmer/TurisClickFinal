using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Shared.Scheduling;

/// <summary>
/// Patrones de alta masiva de disponibilidad (calendario del proveedor). El modelo sigue siendo de fechas
/// concretas (ExperienceAvailability/PackageAvailability, docs/domain-model.md §6): una regla recurrente
/// NO se persiste, se expande una sola vez a filas explícitas dentro de un rango acotado. Así cada fecha
/// sigue teniendo su propio cupo y sus reservas, y editar el "patrón" nunca toca en cascada fechas que ya
/// tienen gente reservada.
/// </summary>
public static class AvailabilitySchedule
{
    /// <summary>Rango máximo de una generación: un año. Más allá, el proveedor vuelve a generar cuando haga falta.</summary>
    public const int MaxRangeDays = 366;

    /// <summary>Techo de filas por request — protege el plan F1 y evita un "todos los días × 10 horarios × 1 año" accidental.</summary>
    public const int MaxSlotsPerRequest = 1000;

    public const int MaxStartTimesPerRequest = 12;

    public const string EveryDay = "EVERY_DAY";
    public const string Weekdays = "WEEKDAYS";
    public const string Weekends = "WEEKENDS";
    public const string Custom = "CUSTOM";

    public static readonly IReadOnlyList<string> Presets = [EveryDay, Weekdays, Weekends, Custom];

    /// <summary>
    /// Los presets son atajos que seleccionan días; CUSTOM usa exactamente los días enviados. Los días se
    /// reciben como el entero de <see cref="DayOfWeek"/> (0 = domingo … 6 = sábado), igual que en JavaScript.
    /// </summary>
    public static HashSet<DayOfWeek> ResolveWeekdays(string? preset, IEnumerable<int>? weekdays)
    {
        var normalized = string.IsNullOrWhiteSpace(preset) ? Custom : preset.Trim().ToUpperInvariant();

        switch (normalized)
        {
            case EveryDay:
                return [.. Enum.GetValues<DayOfWeek>()];
            case Weekdays:
                return [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];
            case Weekends:
                return [DayOfWeek.Saturday, DayOfWeek.Sunday];
            case Custom:
                var days = new HashSet<DayOfWeek>();
                foreach (var day in weekdays ?? [])
                {
                    if (day is < 0 or > 6)
                        throw new ValidationAppException("Los días de la semana van de 0 (domingo) a 6 (sábado).");
                    days.Add((DayOfWeek)day);
                }
                if (days.Count == 0)
                    throw new ValidationAppException("Elegí al menos un día de la semana.");
                return days;
            default:
                throw new ValidationAppException($"Patrón desconocido: {preset}. Valores válidos: {string.Join(", ", Presets)}.");
        }
    }

    /// <summary>Valida el rango: no hacia atrás, no en el pasado, no más de un año.</summary>
    public static void EnsureValidRange(DateOnly startDate, DateOnly endDate, DateOnly today)
    {
        if (endDate < startDate)
            throw new ValidationAppException("La fecha final no puede ser anterior a la inicial.");
        if (startDate < today)
            throw new ValidationAppException("No se puede generar disponibilidad en fechas pasadas.");
        if (endDate.DayNumber - startDate.DayNumber + 1 > MaxRangeDays)
            throw new ValidationAppException($"El rango no puede superar {MaxRangeDays} días.");
    }

    /// <summary>Fechas del rango (ambos extremos incluidos) que caen en los días elegidos, en orden.</summary>
    public static List<DateOnly> ExpandDates(DateOnly startDate, DateOnly endDate, IReadOnlySet<DayOfWeek> weekdays)
    {
        var dates = new List<DateOnly>();
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            if (weekdays.Contains(date.DayOfWeek))
                dates.Add(date);
        }
        return dates;
    }

    /// <summary>Horarios sin repetir y ordenados; lista vacía significa "día completo" (StartTime nulo).</summary>
    public static List<TimeOnly?> NormalizeStartTimes(IEnumerable<TimeOnly>? startTimes)
    {
        var distinct = (startTimes ?? []).Distinct().Order().ToList();
        if (distinct.Count > MaxStartTimesPerRequest)
            throw new ValidationAppException($"Se admiten hasta {MaxStartTimesPerRequest} horarios por generación.");
        return distinct.Count == 0 ? [null] : [.. distinct.Select(t => (TimeOnly?)t)];
    }

    public static void EnsureWithinLimit(int slotCount)
    {
        if (slotCount == 0)
            throw new ValidationAppException("El patrón elegido no produce ninguna fecha dentro del rango.");
        if (slotCount > MaxSlotsPerRequest)
            throw new ValidationAppException(
                $"El patrón genera {slotCount} disponibilidades; el máximo por operación es {MaxSlotsPerRequest}. Acortá el rango o reducí horarios.");
    }
}
