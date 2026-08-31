using TurisClick.Api.Modules.Experiences.Entities;

namespace TurisClick.Api.Modules.Packages.Entities;

/// <summary>
/// docs/domain-model.md §6. Misma forma que ExperienceAvailability pero a nivel de fecha de salida del
/// paquete completo — reutiliza AvailabilitySlotStatus (mismo ENUM de Postgres `availability_slot_status`).
/// </summary>
public class PackageAvailability
{
    public Guid Id { get; set; }
    public Guid PackageId { get; set; }
    public Package? Package { get; set; }

    public DateOnly DepartureDate { get; set; }

    public int TotalSlots { get; set; }

    /// <summary>Contador para el descuento atómico (UC-SYS-06) — nunca se decrementa/incrementa fuera de una transacción condicional.</summary>
    public int ReservedSlots { get; set; }

    public AvailabilitySlotStatus Status { get; set; } = AvailabilitySlotStatus.OPEN;

    public int AvailableSlots => TotalSlots - ReservedSlots;
}
