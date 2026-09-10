using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Ai.Dtos;
using TurisClick.Api.Modules.Ai.Entities;
using TurisClick.Api.Modules.Ai.Repositories;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Packages.Entities;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Modules.Reservations.Repositories;
using TurisClick.Api.Modules.Reservations.Services;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Ai.Services;

public class AiItineraryBookingService(
    IAiItineraryRepository itineraryRepository,
    IAiConversationRepository conversationRepository,
    IAiCatalogRepository catalogRepository,
    IReservationRepository reservationRepository,
    IReservationBookingService reservationBookingService,
    IReservationService reservationService,
    ICurrentUserContext currentUser,
    ILogger<AiItineraryBookingService> logger,
    TurisClickDbContext db) : IAiItineraryBookingService
{
    /// <summary>SQLSTATE de Postgres para violación de restricción única (23505).</summary>
    private const string PostgresUniqueViolation = "23505";

    /// <summary>
    /// Único punto donde se abre la transacción. Es virtual solo para que los tests unitarios puedan
    /// cubrir el camino feliz sin una base real; en producción siempre es la transacción de EF/Npgsql.
    /// La atomicidad de verdad (holds, rollback, concurrencia) se prueba contra PostgreSQL.
    /// </summary>
    protected virtual Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) =>
        db.Database.BeginTransactionAsync(ct);

    public async Task<BookItineraryResponse> BookAsync(Guid itineraryId, BookItineraryRequest request, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        var itinerary = await itineraryRepository.GetByIdForBookingAsync(itineraryId, ct)
            ?? throw new NotFoundAppException("Itinerario no encontrado.");

        if (itinerary.TouristId != currentUser.UserId)
            throw new ForbiddenAppException("Este itinerario no te pertenece.");

        await EnsureBookableStatusAsync(itinerary, ct);

        if (itinerary.Items.Count == 0)
            throw new ValidationAppException("El itinerario no tiene componentes para reservar.", ErrorCodes.ItineraryEmpty);

        var conversation = await conversationRepository.GetByIdForReadAsync(itinerary.AiConversationId, ct);
        var travelers = Math.Max(conversation?.TravelersCount ?? 1, 1);

        // UC-SYS-01/02 — estado ACTUAL de cada producto referenciado. Sin filtrar por PUBLISHED: hace
        // falta poder distinguir "no existe" de "existe pero se despublicó".
        var experiences = (await catalogRepository.GetExperiencesByIdsAsync(
            [.. itinerary.Items.Where(i => i.ExperienceId.HasValue).Select(i => i.ExperienceId!.Value).Distinct()], ct))
            .ToDictionary(e => e.Id);
        var packages = (await catalogRepository.GetPackagesByIdsAsync(
            [.. itinerary.Items.Where(i => i.PackageId.HasValue).Select(i => i.PackageId!.Value).Distinct()], ct))
            .ToDictionary(p => p.Id);

        var lines = new List<BookingLine>();
        var changes = new List<ItineraryPriceChangeResponse>();

        foreach (var item in itinerary.Items.OrderBy(i => i.DayNumber).ThenBy(i => i.SortOrder))
        {
            var (line, change) = item.ProductType == ProductType.EXPERIENCE
                ? ValidateExperienceItem(item, experiences, travelers)
                : ValidatePackageItem(item, packages, travelers);

            lines.Add(line);
            if (change is not null) changes.Add(change);
        }

        // Misma política que UC-T-19: si cambió algo respecto de lo que el turista vio, no se toma
        // ningún cupo ni se crea nada hasta que lo acepte explícitamente.
        if (changes.Count > 0 && !request.AcceptPriceChanges)
        {
            logger.LogInformation(
                "Booking del itinerario {ItineraryId} detenido: {Count} componente(s) cambiaron de precio o moneda y no fueron aceptados.",
                itinerary.Id, changes.Count);

            return new BookItineraryResponse { RequiresPriceAcceptance = true, Changes = changes };
        }

        var reservationId = await ExecuteBookingTransactionAsync(itinerary, lines, ct);

        stopwatch.Stop();
        logger.LogInformation(
            "Booking del itinerario {ItineraryId} completado: reserva {ReservationId}, {Items} ítem(s), {Companies} empresa(s), {PriceChanges} cambio(s) de precio aceptados, {ElapsedMs} ms.",
            itinerary.Id, reservationId, lines.Count, lines.Select(l => l.CompanyId).Distinct().Count(), changes.Count, stopwatch.ElapsedMilliseconds);

        return new BookItineraryResponse
        {
            Reservation = await reservationService.GetByIdForTouristAsync(reservationId, ct),
            Changes = changes
        };
    }

    /// <summary>
    /// Toda la unidad atómica de UC-T-18: holds de cupo + Reservation + ReservationItems + vínculo con
    /// el itinerario + transición a BOOKED. Si algo falla, el rollback deshace también los holds ya
    /// tomados — nunca queda una reserva parcial ni un itinerario BOOKED sin reserva.
    /// </summary>
    private async Task<Guid> ExecuteBookingTransactionAsync(
        AiItinerary itinerary, List<BookingLine> lines, CancellationToken ct)
    {
        await using var tx = await BeginTransactionAsync(ct);

        try
        {
            var reservation = await reservationBookingService.HoldAndBuildAsync(
                itinerary.TouristId, itinerary.Id, lines, ct);

            // BOOKED solo dentro de la transacción que ya tomó los cupos: nunca antes de que todo salga bien.
            itinerary.Status = AiItineraryStatus.BOOKED;
            itinerary.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return reservation.Id;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresUniqueViolation })
        {
            // Doble click / doble request concurrente: los dos leyeron el itinerario todavía reservable
            // y los dos intentaron insertar su reserva. El índice único parcial sobre
            // reservations.ai_itinerary_id (migración 0007) hace que solo uno gane; el otro llega acá.
            await tx.RollbackAsync(ct);

            logger.LogWarning(
                "Booking concurrente del itinerario {ItineraryId}: otra reserva ganó la carrera, se descarta esta.", itinerary.Id);

            var existing = await reservationRepository.GetByAiItineraryIdAsync(itinerary.Id, ct);
            throw new ConflictAppException(
                existing is null
                    ? "Este itinerario ya fue reservado."
                    : $"Este itinerario ya fue reservado (reserva {existing.Id}).",
                ErrorCodes.ItineraryAlreadyBooked);
        }
    }

    /// <summary>DRAFT y SAVED se pueden reservar: UC-T-18 solo exige que el itinerario tenga componentes, no que esté guardado.</summary>
    private async Task EnsureBookableStatusAsync(AiItinerary itinerary, CancellationToken ct)
    {
        switch (itinerary.Status)
        {
            case AiItineraryStatus.DRAFT:
            case AiItineraryStatus.SAVED:
                return;

            case AiItineraryStatus.BOOKED:
                var existing = await reservationRepository.GetByAiItineraryIdAsync(itinerary.Id, ct);
                throw new ConflictAppException(
                    existing is null
                        ? "Este itinerario ya fue reservado."
                        : $"Este itinerario ya fue reservado (reserva {existing.Id}).",
                    ErrorCodes.ItineraryAlreadyBooked);

            default:
                throw new ConflictAppException(
                    $"Un itinerario en estado {itinerary.Status} no se puede reservar.",
                    ErrorCodes.InvalidItineraryStatus);
        }
    }

    private static (BookingLine Line, ItineraryPriceChangeResponse? Change) ValidateExperienceItem(
        AiItineraryItem item, IReadOnlyDictionary<Guid, Experience> experiences, int travelers)
    {
        if (item.ExperienceId is not { } experienceId || !experiences.TryGetValue(experienceId, out var experience))
            throw new ConflictAppException("Una de las experiencias del itinerario ya no existe en el catálogo.", ErrorCodes.ProductUnavailable);

        if (experience.Status != PublicationStatus.PUBLISHED)
            throw new ConflictAppException($"\"{experience.Title}\" ya no está publicada.", ErrorCodes.ProductUnavailable);

        // Regla de negocio 14 (domain-model.md): antes de reservar, todo ítem tiene que tener resuelto
        // su slot. No se elige uno por el turista — se le pide que regenere/ajuste la propuesta.
        if (item.ExperienceAvailabilityId is not { } availabilityId)
            throw new ConflictAppException(
                $"\"{experience.Title}\" no tiene una fecha concreta asignada en la propuesta.", ErrorCodes.AvailabilityNotResolved);

        var availability = experience.Availabilities.FirstOrDefault(a => a.Id == availabilityId)
            ?? throw new ConflictAppException(
                $"La fecha propuesta para \"{experience.Title}\" ya no existe.", ErrorCodes.ProductUnavailable);

        if (availability.Status != AvailabilitySlotStatus.OPEN)
            throw new ConflictAppException($"\"{experience.Title}\" cerró la disponibilidad del {availability.Date:yyyy-MM-dd}.", ErrorCodes.ProductUnavailable);

        if (availability.Date < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ConflictAppException($"La fecha propuesta para \"{experience.Title}\" ya pasó.", ErrorCodes.ProductUnavailable);

        // Chequeo de UX: el hold condicional sigue siendo la autoridad final sobre el cupo.
        if (availability.AvailableSlots < travelers)
            throw new ConflictAppException(
                $"\"{experience.Title}\" no tiene cupo para {travelers} viajero(s).", ErrorCodes.InsufficientCapacity);

        var line = new BookingLine(
            ProductType.EXPERIENCE, experience.Id, availability.Id,
            // CompanyId derivado del producto real, nunca de algo que mande el cliente.
            experience.CompanyId, travelers, experience.Price, experience.Currency, item.DayNumber);

        return (line, BuildChange(item, experience.Title, experience.Price, experience.Currency));
    }

    private static (BookingLine Line, ItineraryPriceChangeResponse? Change) ValidatePackageItem(
        AiItineraryItem item, IReadOnlyDictionary<Guid, Package> packages, int travelers)
    {
        if (item.PackageId is not { } packageId || !packages.TryGetValue(packageId, out var package))
            throw new ConflictAppException("Uno de los paquetes del itinerario ya no existe en el catálogo.", ErrorCodes.ProductUnavailable);

        if (package.Status != PublicationStatus.PUBLISHED)
            throw new ConflictAppException($"El paquete \"{package.Title}\" ya no está publicado.", ErrorCodes.ProductUnavailable);

        if (item.PackageAvailabilityId is not { } availabilityId)
            throw new ConflictAppException(
                $"El paquete \"{package.Title}\" no tiene una salida concreta asignada en la propuesta.", ErrorCodes.AvailabilityNotResolved);

        var availability = package.Availabilities.FirstOrDefault(a => a.Id == availabilityId)
            ?? throw new ConflictAppException(
                $"La salida propuesta para \"{package.Title}\" ya no existe.", ErrorCodes.ProductUnavailable);

        if (availability.Status != AvailabilitySlotStatus.OPEN)
            throw new ConflictAppException($"El paquete \"{package.Title}\" cerró la salida del {availability.DepartureDate:yyyy-MM-dd}.", ErrorCodes.ProductUnavailable);

        if (availability.DepartureDate < DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ConflictAppException($"La salida propuesta para \"{package.Title}\" ya pasó.", ErrorCodes.ProductUnavailable);

        if (availability.AvailableSlots < travelers)
            throw new ConflictAppException(
                $"El paquete \"{package.Title}\" no tiene cupo para {travelers} viajero(s).", ErrorCodes.InsufficientCapacity);

        var line = new BookingLine(
            ProductType.PACKAGE, package.Id, availability.Id,
            package.CompanyId, travelers, package.Price, package.Currency, item.DayNumber);

        return (line, BuildChange(item, package.Title, package.Price, package.Currency));
    }

    /// <summary>
    /// Compara el snapshot que vio el turista contra el precio vigente. Un cambio de moneda se reporta
    /// como CURRENCY_CHANGED y NUNCA como una diferencia numérica: comparar 100 USD con 100 BOB exigiría
    /// una conversión que TurisClick no hace.
    /// </summary>
    private static ItineraryPriceChangeResponse? BuildChange(
        AiItineraryItem item, string title, decimal currentPrice, string currentCurrency)
    {
        var currencyChanged = !string.Equals(currentCurrency, item.Currency, StringComparison.OrdinalIgnoreCase);
        if (!currencyChanged && currentPrice == item.EstimatedUnitPrice)
            return null;

        return new ItineraryPriceChangeResponse
        {
            ItineraryItemId = item.Id,
            ProductType = item.ProductType.ToString(),
            ProductTitle = title,
            ChangeType = currencyChanged ? ErrorCodes.CurrencyChanged : ErrorCodes.PriceChanged,
            PreviousUnitPrice = item.EstimatedUnitPrice,
            PreviousCurrency = item.Currency,
            CurrentUnitPrice = currentPrice,
            CurrentCurrency = currentCurrency
        };
    }
}
