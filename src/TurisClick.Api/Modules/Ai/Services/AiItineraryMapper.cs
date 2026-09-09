using TurisClick.Api.Modules.Ai.Dtos;
using TurisClick.Api.Modules.Ai.Entities;

namespace TurisClick.Api.Modules.Ai.Services;

/// <summary>
/// Mapeo compartido entre AiConversationService (UC-T-14) y AiItineraryService (UC-T-16/17) — el
/// itinerario se ve igual se llegue por la conversación o por "Mis itinerarios guardados".
/// </summary>
public static class AiItineraryMapper
{
    public static ItineraryResponse ToResponse(AiItinerary itinerary, int travelers, ItineraryRevalidationResult revalidation)
    {
        var items = itinerary.Items
            .OrderBy(i => i.DayNumber).ThenBy(i => i.SortOrder)
            .Select(i =>
            {
                revalidation.ByItemId.TryGetValue(i.Id, out var live);

                return new ItineraryItemResponse
                {
                    Id = i.Id,
                    DayNumber = i.DayNumber,
                    SortOrder = i.SortOrder,
                    ProductType = i.ProductType.ToString(),
                    ExperienceId = i.ExperienceId,
                    ExperienceTitle = i.Experience?.Title,
                    PackageId = i.PackageId,
                    PackageTitle = i.Package?.Title,
                    Date = i.ExperienceAvailability?.Date ?? i.PackageAvailability?.DepartureDate,
                    // Snapshot histórico: es lo que se le propuso al turista y NUNCA se reescribe.
                    EstimatedUnitPrice = i.EstimatedUnitPrice,
                    Currency = i.Currency,
                    Travelers = travelers,
                    Subtotal = i.EstimatedUnitPrice * travelers,
                    // Estado vigente, al lado del snapshot: los dos coexisten en la misma respuesta.
                    CurrentPrice = live?.CurrentPrice,
                    CurrentCurrency = live?.CurrentCurrency,
                    CurrentAvailableSlots = live?.AvailableSlots,
                    PriceChanged = live?.PriceChanged(i.EstimatedUnitPrice, i.Currency) ?? false,
                    // Sin revalidación (ej. justo después de generar, ya validado en ese mismo request)
                    // no se afirma nada negativo: se asume vigente.
                    AvailabilityState = (live?.State ?? ItemAvailabilityState.AVAILABLE).ToString(),
                    IsStillAvailable = live?.IsValid ?? true,
                    Warnings = live?.Warnings.ToList() ?? []
                };
            })
            .ToList();

        return new ItineraryResponse
        {
            Id = itinerary.Id,
            AiConversationId = itinerary.AiConversationId,
            Title = itinerary.Title,
            Status = itinerary.Status.ToString(),
            Version = itinerary.Version,
            Items = items,
            // Totales sobre el SNAPSHOT (lo que se le propuso al turista); el precio vigente viaja por
            // ítem en CurrentPrice para que la diferencia sea explícita y no una suma silenciosa.
            Totals = [.. itinerary.Items
                .GroupBy(i => i.Currency)
                .Select(g => new ItineraryTotalResponse { Currency = g.Key, Amount = g.Sum(i => i.EstimatedUnitPrice * travelers) })],
            Warnings = revalidation.Warnings.ToList(),
            IsStillBookable = items.Count > 0 && items.All(i => i.IsStillAvailable),
            CreatedAt = itinerary.CreatedAt,
            UpdatedAt = itinerary.UpdatedAt
        };
    }

    public static SavedItinerarySummaryResponse ToSummary(AiItinerary itinerary, int travelers) => new()
    {
        Id = itinerary.Id,
        AiConversationId = itinerary.AiConversationId,
        Title = itinerary.Title,
        Status = itinerary.Status.ToString(),
        Version = itinerary.Version,
        ItemCount = itinerary.Items.Count,
        Totals = [.. itinerary.Items
            .GroupBy(i => i.Currency)
            .Select(g => new ItineraryTotalResponse { Currency = g.Key, Amount = g.Sum(i => i.EstimatedUnitPrice * travelers) })],
        CreatedAt = itinerary.CreatedAt,
        UpdatedAt = itinerary.UpdatedAt
    };
}
