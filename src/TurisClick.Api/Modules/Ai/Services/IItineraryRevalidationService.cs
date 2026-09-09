using TurisClick.Api.Modules.Ai.Entities;

namespace TurisClick.Api.Modules.Ai.Services;

/// <summary>
/// UC-T-15/17 (secciones 3 y 9 de la sesión) — contrasta el snapshot persistido de cada
/// <see cref="AiItineraryItem"/> contra el estado ACTUAL del producto en Postgres.
///
/// Nunca modifica el snapshot: guardar un itinerario no congela precio ni retiene cupo (eso recién
/// pasa al reservar, UC-T-18/Oleada 7), así que el snapshot se conserva como referencia histórica y el
/// estado vigente se devuelve al lado para que el turista vea la diferencia explícitamente.
/// </summary>
public interface IItineraryRevalidationService
{
    Task<ItineraryRevalidationResult> RevalidateAsync(AiItinerary itinerary, CancellationToken ct);
}

/// <summary>
/// Por qué un componente ya no se puede reservar — dato estructurado para el cliente, que no debería
/// tener que parsear los warnings en prosa para saber qué pasó.
/// </summary>
public enum ItemAvailabilityState
{
    AVAILABLE,
    /// <summary>El producto ya no existe en el catálogo.</summary>
    PRODUCT_NOT_FOUND,
    /// <summary>El proveedor lo despublicó (o quedó suspendido).</summary>
    UNPUBLISHED,
    /// <summary>El slot propuesto ya no existe o el proveedor lo cerró.</summary>
    SLOT_CLOSED,
    /// <summary>Sigue publicado y abierto, pero se quedó sin cupos.</summary>
    SOLD_OUT
}

/// <summary>Estado vigente de un ítem. <c>IsValid</c> es la respuesta a "¿esto todavía se podría reservar?".</summary>
public record ItemRevalidation(
    Guid ItemId,
    bool ProductExists,
    bool IsPublished,
    bool AvailabilityExists,
    bool HasCapacity,
    decimal? CurrentPrice,
    string? CurrentCurrency,
    int? AvailableSlots,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Se reporta la causa más de fondo primero (despublicado pesa más que sin cupos).</summary>
    public ItemAvailabilityState State =>
        !ProductExists ? ItemAvailabilityState.PRODUCT_NOT_FOUND
        : !IsPublished ? ItemAvailabilityState.UNPUBLISHED
        : !AvailabilityExists ? ItemAvailabilityState.SLOT_CLOSED
        : !HasCapacity ? ItemAvailabilityState.SOLD_OUT
        : ItemAvailabilityState.AVAILABLE;

    public bool IsValid => State == ItemAvailabilityState.AVAILABLE;

    /// <summary>true solo si se puede comparar de verdad: mismo código de moneda (nunca se convierte, sección 10).</summary>
    public bool PriceChanged(decimal snapshotPrice, string snapshotCurrency) =>
        CurrentPrice is { } current
        && string.Equals(CurrentCurrency, snapshotCurrency, StringComparison.OrdinalIgnoreCase)
        && current != snapshotPrice;
}

public record ItineraryRevalidationResult(
    IReadOnlyDictionary<Guid, ItemRevalidation> ByItemId,
    IReadOnlyList<string> Warnings)
{
    public static readonly ItineraryRevalidationResult Empty =
        new(new Dictionary<Guid, ItemRevalidation>(), []);
}
