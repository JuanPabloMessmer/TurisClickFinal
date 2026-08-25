namespace TurisClick.Api.Modules.Experiences.Entities;

/// <summary>docs/domain-model.md §6. Fechas/slots concretos (decisión 8) — sin recurrencia todavía.</summary>
public class ExperienceAvailability
{
    public Guid Id { get; set; }
    public Guid ExperienceId { get; set; }
    public Experience? Experience { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>Nulo = disponibilidad de día completo (nunca se infiere una hora aproximada).</summary>
    public TimeOnly? StartTime { get; set; }

    public int TotalSlots { get; set; }

    /// <summary>Contador para el descuento atómico (UC-SYS-06) — nunca se decrementa/incrementa fuera de una transacción condicional.</summary>
    public int ReservedSlots { get; set; }

    public AvailabilitySlotStatus Status { get; set; } = AvailabilitySlotStatus.OPEN;

    public int AvailableSlots => TotalSlots - ReservedSlots;
}
