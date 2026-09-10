using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Modules.Reservations.Repositories;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Reservations.Services;

public class ReservationBookingService(
    IReservationRepository reservationRepository,
    ILogger<ReservationBookingService> logger,
    TurisClickDbContext db) : IReservationBookingService
{
    /// <summary>Misma ventana de hold que una reserva directa — el booking desde IA no inventa su propia política de expiración.</summary>
    public static readonly TimeSpan HoldWindow = TimeSpan.FromMinutes(30);

    public async Task<Reservation> HoldAndBuildAsync(
        Guid touristId, Guid? aiItineraryId, IReadOnlyList<BookingLine> lines, CancellationToken ct)
    {
        if (lines.Count == 0)
            throw new ValidationAppException("No hay nada para reservar.", ErrorCodes.ItineraryEmpty);

        // Dos ítems del itinerario pueden apuntar al MISMO slot (ej. dos actividades del mismo día en
        // la misma salida). Si se hiciera un UPDATE por ítem, cada uno evaluaría la condición de
        // capacidad por separado y podría pasar la suma total. Se agrupa y se toma el cupo de una vez.
        var holds = lines
            .GroupBy(l => (l.ProductType, l.AvailabilityId))
            .Select(g => new
            {
                g.Key.ProductType,
                g.Key.AvailabilityId,
                Travelers = g.Sum(l => l.Travelers)
            })
            // Orden determinístico (tipo, luego id): dos bookings concurrentes que compiten por las
            // mismas availabilities las toman siempre en el mismo orden, lo que evita el deadlock
            // clásico de "A espera a B mientras B espera a A" sin necesidad de locks externos.
            .OrderBy(h => h.ProductType)
            .ThenBy(h => h.AvailabilityId)
            .ToList();

        foreach (var hold in holds)
        {
            var affectedRows = hold.ProductType == ProductType.EXPERIENCE
                ? await db.ExperienceAvailabilities
                    .Where(a => a.Id == hold.AvailabilityId
                        && a.Status == AvailabilitySlotStatus.OPEN
                        && a.ReservedSlots + hold.Travelers <= a.TotalSlots)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReservedSlots, a => a.ReservedSlots + hold.Travelers), ct)
                : await db.PackageAvailabilities
                    .Where(a => a.Id == hold.AvailabilityId
                        && a.Status == AvailabilitySlotStatus.OPEN
                        && a.ReservedSlots + hold.Travelers <= a.TotalSlots)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReservedSlots, a => a.ReservedSlots + hold.Travelers), ct);

            if (affectedRows != 1)
            {
                // El caller revierte la transacción entera: los holds anteriores de este mismo booking
                // se deshacen con el rollback, así que nunca queda una reserva parcial.
                logger.LogWarning(
                    "Booking rechazado: no se pudo tomar {Travelers} cupo(s) de {ProductType} {AvailabilityId}.",
                    hold.Travelers, hold.ProductType, hold.AvailabilityId);

                throw new ConflictAppException(
                    "Uno de los componentes ya no tiene cupo suficiente; no se reservó nada.",
                    ErrorCodes.InsufficientCapacity);
            }
        }

        var now = DateTimeOffset.UtcNow;

        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            TouristId = touristId,
            AiItineraryId = aiItineraryId,
            Status = ReservationStatus.PENDING_PAYMENT,
            ExpiresAt = now.Add(HoldWindow),
            CreatedAt = now
        };

        foreach (var line in lines)
        {
            reservation.Items.Add(new ReservationItem
            {
                Id = Guid.NewGuid(),
                CompanyId = line.CompanyId,
                ProductType = line.ProductType,
                ExperienceId = line.ProductType == ProductType.EXPERIENCE ? line.ProductId : null,
                PackageId = line.ProductType == ProductType.PACKAGE ? line.ProductId : null,
                ExperienceAvailabilityId = line.ProductType == ProductType.EXPERIENCE ? line.AvailabilityId : null,
                PackageAvailabilityId = line.ProductType == ProductType.PACKAGE ? line.AvailabilityId : null,
                Travelers = line.Travelers,
                // Snapshot COMERCIAL propio de la reserva, distinto del snapshot histórico del
                // AiItineraryItem: acá se congela el precio vigente al momento de reservar.
                UnitPrice = line.UnitPrice,
                Currency = line.Currency,
                Subtotal = line.UnitPrice * line.Travelers,
                Status = ReservationItemStatus.PENDING_PAYMENT,
                DayNumber = line.DayNumber,
                CreatedAt = now
            });
        }

        await reservationRepository.AddAsync(reservation, ct);

        logger.LogInformation(
            "Reserva {ReservationId} armada con {Items} ítem(s) de {Companies} empresa(s), {Holds} hold(s) de cupo.",
            reservation.Id, reservation.Items.Count, reservation.Items.Select(i => i.CompanyId).Distinct().Count(), holds.Count);

        return reservation;
    }
}
