using System.Text.Json.Serialization;

namespace TurisClick.Api.Modules.Flights.Services.Providers.Duffel;

/// <summary>
/// Forma de los mensajes de Duffel. **Internos a propósito**: ningún tipo de este archivo sale del
/// adapter — el resto de TurisClick sólo conoce los modelos de FlightModels.cs.
///
/// Duffel envuelve todo en `data` y publica los importes como string ("123.45"), no como número: el
/// mapeo los convierte una sola vez, acá.
/// </summary>
internal sealed record DuffelEnvelope<T>([property: JsonPropertyName("data")] T? Data);

internal sealed record DuffelErrorEnvelope(
    [property: JsonPropertyName("errors")] List<DuffelError>? Errors);

internal sealed record DuffelError(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("code")] string? Code);

// ---------------------------------------------------------------- búsqueda

internal sealed record DuffelOfferRequestBody(
    [property: JsonPropertyName("slices")] List<DuffelSliceRequest> Slices,
    [property: JsonPropertyName("passengers")] List<DuffelPassengerRequest> Passengers,
    [property: JsonPropertyName("cabin_class")] string CabinClass);

internal sealed record DuffelSliceRequest(
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("destination")] string Destination,
    [property: JsonPropertyName("departure_date")] string DepartureDate);

internal sealed record DuffelPassengerRequest([property: JsonPropertyName("type")] string Type);

internal sealed record DuffelOfferRequest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("offers")] List<DuffelOffer>? Offers);

// ---------------------------------------------------------------- oferta

internal sealed record DuffelOffer(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("total_amount")] string? TotalAmount,
    [property: JsonPropertyName("total_currency")] string? TotalCurrency,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("live_mode")] bool LiveMode,
    [property: JsonPropertyName("passenger_identity_documents_required")] bool IdentityDocumentsRequired,
    [property: JsonPropertyName("owner")] DuffelAirline? Owner,
    [property: JsonPropertyName("slices")] List<DuffelSlice>? Slices,
    [property: JsonPropertyName("passengers")] List<DuffelOfferPassenger>? Passengers,
    [property: JsonPropertyName("payment_requirements")] DuffelPaymentRequirements? PaymentRequirements);

internal sealed record DuffelPaymentRequirements(
    [property: JsonPropertyName("requires_instant_payment")] bool RequiresInstantPayment,
    [property: JsonPropertyName("payment_required_by")] DateTimeOffset? PaymentRequiredBy,
    [property: JsonPropertyName("price_guarantee_expires_at")] DateTimeOffset? PriceGuaranteeExpiresAt);

internal sealed record DuffelAirline(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("iata_code")] string? IataCode);

internal sealed record DuffelOfferPassenger(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string? Type);

internal sealed record DuffelSlice(
    [property: JsonPropertyName("origin")] DuffelPlace? Origin,
    [property: JsonPropertyName("destination")] DuffelPlace? Destination,
    [property: JsonPropertyName("duration")] string? Duration,
    [property: JsonPropertyName("segments")] List<DuffelSegment>? Segments);

internal sealed record DuffelPlace([property: JsonPropertyName("iata_code")] string? IataCode);

internal sealed record DuffelSegment(
    [property: JsonPropertyName("origin")] DuffelPlace? Origin,
    [property: JsonPropertyName("destination")] DuffelPlace? Destination,
    [property: JsonPropertyName("departing_at")] DateTime? DepartingAt,
    [property: JsonPropertyName("arriving_at")] DateTime? ArrivingAt,
    [property: JsonPropertyName("marketing_carrier")] DuffelAirline? MarketingCarrier,
    [property: JsonPropertyName("marketing_carrier_flight_number")] string? FlightNumber,
    [property: JsonPropertyName("aircraft")] DuffelAircraft? Aircraft,
    [property: JsonPropertyName("passengers")] List<DuffelSegmentPassenger>? Passengers);

internal sealed record DuffelAircraft([property: JsonPropertyName("name")] string? Name);

internal sealed record DuffelSegmentPassenger(
    [property: JsonPropertyName("baggages")] List<DuffelBaggage>? Baggages);

internal sealed record DuffelBaggage(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("quantity")] int Quantity);

// ---------------------------------------------------------------- orden

internal sealed record DuffelCreateOrderBody(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("selected_offers")] List<string> SelectedOffers,
    [property: JsonPropertyName("payments")] List<DuffelPayment> Payments,
    [property: JsonPropertyName("passengers")] List<DuffelOrderPassenger> Passengers);

/// <summary>
/// En modo de prueba el saldo de la cuenta es ilimitado y el pago se declara como `balance`: no hay
/// tarjeta, no hay dinero. El pago simulado de TurisClick es otra cosa y vive en su propio módulo.
/// </summary>
internal sealed record DuffelPayment(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("amount")] string Amount);

internal sealed record DuffelOrderPassenger(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("given_name")] string GivenName,
    [property: JsonPropertyName("family_name")] string FamilyName,
    [property: JsonPropertyName("born_on")] string BornOn,
    [property: JsonPropertyName("gender")] string Gender,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("phone_number")] string PhoneNumber);

internal sealed record DuffelOrder(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("booking_reference")] string? BookingReference,
    [property: JsonPropertyName("total_amount")] string? TotalAmount,
    [property: JsonPropertyName("total_currency")] string? TotalCurrency,
    [property: JsonPropertyName("live_mode")] bool LiveMode,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt,
    [property: JsonPropertyName("slices")] List<DuffelSlice>? Slices);

// ---------------------------------------------------------------- cancelación

internal sealed record DuffelCancellationBody(
    [property: JsonPropertyName("order_id")] string OrderId);

internal sealed record DuffelOrderCancellation(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("refund_amount")] string? RefundAmount,
    [property: JsonPropertyName("refund_currency")] string? RefundCurrency,
    [property: JsonPropertyName("refund_to")] string? RefundTo,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("confirmed_at")] DateTimeOffset? ConfirmedAt);
