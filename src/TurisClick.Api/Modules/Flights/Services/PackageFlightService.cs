using Microsoft.EntityFrameworkCore;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Infrastructure.Security;
using TurisClick.Api.Modules.Experiences.Entities;
using TurisClick.Api.Modules.Flights.Dtos;
using TurisClick.Api.Modules.Flights.Entities;
using TurisClick.Api.Modules.Packages.Entities;
using TurisClick.Api.Shared.Exceptions;

namespace TurisClick.Api.Modules.Flights.Services;

public interface IPackageFlightService
{
    Task<PackageFlightRuleResponse> SetRuleAsync(Guid packageId, PackageFlightRuleRequest request, CancellationToken ct);
    Task RemoveRuleAsync(Guid packageId, CancellationToken ct);
    Task<PackageFlightRuleResponse?> GetRuleAsync(Guid packageId, CancellationToken ct);
    Task<PackageFlightQuoteResponse> QuoteAsync(Guid packageId, PackageFlightQuoteRequest request, CancellationToken ct);
    Task<FlightQuoteRevalidationResponse> RevalidateAsync(Guid quoteId, CancellationToken ct);
}

/// <summary>
/// Reglas de vuelo de un paquete y cotización contra el proveedor.
///
/// Dos invariantes que este servicio sostiene y que conviene no perder:
///
/// 1. **El servidor es la autoridad sobre el precio.** El cliente manda un id de cotización; nunca un
///    importe, una moneda ni un id de oferta del proveedor. Lo que se compara al revalidar es lo que
///    nosotros guardamos, no lo que el cliente dice que le cotizaron.
/// 2. **No se suman monedas distintas.** Si el paquete cotiza en USD y el vuelo en EUR, se muestran los
///    dos importes por separado: inventar un tipo de cambio sería inventar plata.
/// </summary>
public class PackageFlightService(
    TurisClickDbContext db,
    IFlightProvider flightProvider,
    ICurrentUserContext currentUser,
    ICompanyOwnershipGuard ownershipGuard,
    ILogger<PackageFlightService> logger) : IPackageFlightService
{
    /// <summary>Cuántas opciones se le ofrecen al turista. Más que esto es una lista que nadie lee.</summary>
    private const int MaxOptions = 3;

    // ---------------------------------------------------------------- reglas (operador)

    public async Task<PackageFlightRuleResponse> SetRuleAsync(
        Guid packageId, PackageFlightRuleRequest request, CancellationToken ct)
    {
        var package = await db.Packages
            .Include(p => p.FlightRule)
            .FirstOrDefaultAsync(p => p.Id == packageId, ct)
            ?? throw new NotFoundAppException("Paquete no encontrado.");

        ownershipGuard.EnsureOwns(package.CompanyId);

        var destination = AirportCatalog.Normalize(request.DestinationIata);
        var origins = request.AllowedOriginIatas.Select(AirportCatalog.Normalize).Distinct().ToList();

        var rule = package.FlightRule;
        if (rule is null)
        {
            rule = new PackageFlightRule { PackageId = packageId, CreatedAt = DateTimeOffset.UtcNow };
            db.PackageFlightRules.Add(rule);
        }

        rule.DestinationIata = destination;
        rule.AllowedOriginIatas = string.Join(',', origins);
        rule.CabinClass = request.Cabin();
        rule.OutboundOffsetDays = request.OutboundOffsetDays;
        rule.InboundOffsetDays = request.InboundOffsetDays;
        rule.RoundTrip = request.RoundTrip;
        rule.UpdatedAt = DateTimeOffset.UtcNow;

        // El flag del paquete y la regla se mueven juntos: un paquete que "incluye vuelo" sin regla no
        // se puede cotizar, y una regla sin flag no se usa nunca.
        package.IncludesFlight = true;
        package.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Regla de vuelo guardada para el paquete {PackageId} ({Origins} → {Destination}).",
            packageId, rule.AllowedOriginIatas, rule.DestinationIata);

        return ToResponse(rule);
    }

    public async Task RemoveRuleAsync(Guid packageId, CancellationToken ct)
    {
        var package = await db.Packages
            .Include(p => p.FlightRule)
            .FirstOrDefaultAsync(p => p.Id == packageId, ct)
            ?? throw new NotFoundAppException("Paquete no encontrado.");

        ownershipGuard.EnsureOwns(package.CompanyId);

        if (package.FlightRule is not null) db.PackageFlightRules.Remove(package.FlightRule);

        package.IncludesFlight = false;
        package.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Lectura de la regla. El ADMIN la ve para cualquier paquete —necesita visibilidad global para
    /// moderar— y el PROVIDER sólo para los suyos.
    /// </summary>
    public async Task<PackageFlightRuleResponse?> GetRuleAsync(Guid packageId, CancellationToken ct)
    {
        var package = await db.Packages
            .AsNoTracking()
            .Include(p => p.FlightRule)
            .FirstOrDefaultAsync(p => p.Id == packageId, ct)
            ?? throw new NotFoundAppException("Paquete no encontrado.");

        if (currentUser.Role != "ADMIN") ownershipGuard.EnsureOwns(package.CompanyId);

        return package.FlightRule is null ? null : ToResponse(package.FlightRule);
    }

    // ---------------------------------------------------------------- cotización (turista)

    public async Task<PackageFlightQuoteResponse> QuoteAsync(
        Guid packageId, PackageFlightQuoteRequest request, CancellationToken ct)
    {
        var package = await db.Packages
            .Include(p => p.FlightRule)
            .Include(p => p.Availabilities)
            .FirstOrDefaultAsync(p => p.Id == packageId, ct)
            ?? throw new NotFoundAppException("Paquete no encontrado.");

        // El catálogo público sólo muestra lo publicado: cotizar un borrador filtraría precios de algo
        // que todavía no está a la venta.
        if (package.Status != PublicationStatus.PUBLISHED)
            throw new NotFoundAppException("Paquete no encontrado.");

        if (!package.IncludesFlight || package.FlightRule is null)
            throw new ValidationAppException("Este paquete no incluye vuelo.");

        var rule = package.FlightRule;
        var origin = AirportCatalog.Normalize(request.OriginIata);

        if (!rule.Origins().Contains(origin))
            throw new ValidationAppException(
                $"El operador no ofrece salida desde {AirportCatalog.Describe(origin)} para este paquete.");

        var availability = package.Availabilities.FirstOrDefault(a => a.Id == request.PackageAvailabilityId)
            ?? throw new NotFoundAppException("La salida elegida no pertenece a este paquete.");

        if (availability.Status != AvailabilitySlotStatus.OPEN || availability.AvailableSlots < request.Travelers)
            throw new ConflictAppException("Esa salida ya no tiene cupo para esa cantidad de viajeros.");

        var dates = FlightDateCalculator.Calculate(rule, availability.DepartureDate, package.DurationDays);
        var slices = FlightDateCalculator.BuildSlices(rule, origin, dates);

        var response = new PackageFlightQuoteResponse
        {
            PackageId = package.Id,
            PackageTitle = package.Title,
            Travelers = request.Travelers,
            PackagePrice = new MoneyResponse { Amount = package.Price, Currency = package.Currency },
            OriginIata = origin,
            OriginLabel = AirportCatalog.Describe(origin),
            DestinationIata = rule.DestinationIata,
            DestinationLabel = AirportCatalog.Describe(rule.DestinationIata),
            OutboundDate = dates.Outbound,
            InboundDate = dates.Inbound,
            CabinClass = rule.CabinClass.ToString(),
        };

        FlightSearchResult search;
        try
        {
            search = await flightProvider.SearchAsync(
                new FlightSearchRequest(slices, request.Travelers, rule.CabinClass, MaxOptions), ct);
        }
        catch (FlightProviderException ex)
        {
            // Que el proveedor falle no rompe la ficha del paquete: se devuelve el precio terrestre y se
            // dice que los vuelos no se pudieron consultar.
            logger.LogWarning(ex, "No se pudieron cotizar vuelos para el paquete {PackageId}.", packageId);
            response.Notice = "No pudimos consultar vuelos en este momento. Probá de nuevo en unos minutos.";
            return response;
        }

        if (search.Offers.Count == 0)
        {
            response.Notice = $"No encontramos vuelos para {dates.Outbound:dd/MM} desde {AirportCatalog.Describe(origin)}.";
            return response;
        }

        var now = DateTimeOffset.UtcNow;
        var quotes = new List<FlightQuote>();

        foreach (var offer in search.Offers.OrderBy(o => o.Price.Amount).Take(MaxOptions))
        {
            var quote = new FlightQuote
            {
                Id = Guid.NewGuid(),
                PackageId = package.Id,
                PackageAvailabilityId = availability.Id,
                // El catálogo es público: se cotiza con o sin sesión, y si la hay queda registrada.
                TouristId = currentUser.IsAuthenticated ? currentUser.UserId : null,
                Provider = flightProvider.Name,
                ProviderOfferId = offer.Id,
                OriginIata = origin,
                DestinationIata = rule.DestinationIata,
                OutboundDate = dates.Outbound,
                InboundDate = dates.Inbound,
                Travelers = request.Travelers,
                TotalAmount = offer.Price.Amount,
                InitialAmount = offer.Price.Amount,
                Currency = offer.Price.Currency,
                ExpiresAt = offer.ExpiresAt,
                QuotedAt = now,
                Status = FlightQuoteStatus.QUOTED,
                ItinerarySummary = Summarize(offer),
            };

            quotes.Add(quote);
            response.Options.Add(ToOption(quote, offer, package));
            response.TestMode |= !offer.LiveMode;
        }

        db.FlightQuotes.AddRange(quotes);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Paquete {PackageId}: {Count} cotización(es) de vuelo {Origin}→{Destination} para {Travelers} viajero(s).",
            packageId, quotes.Count, origin, rule.DestinationIata, request.Travelers);

        return response;
    }

    // ---------------------------------------------------------------- revalidación

    public async Task<FlightQuoteRevalidationResponse> RevalidateAsync(Guid quoteId, CancellationToken ct)
    {
        var quote = await db.FlightQuotes
            .Include(q => q.Package)
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct)
            ?? throw new NotFoundAppException("Cotización no encontrada.");

        var previous = new MoneyResponse { Amount = quote.TotalAmount, Currency = quote.Currency };
        var response = new FlightQuoteRevalidationResponse { QuoteId = quote.Id, PreviousPrice = previous };

        // Si ya venció no hace falta molestar al proveedor: la respuesta es volver a buscar.
        if (quote.IsExpired(DateTimeOffset.UtcNow))
        {
            quote.Status = FlightQuoteStatus.EXPIRED;
            await db.SaveChangesAsync(ct);

            response.Outcome = "EXPIRED";
            response.Message = "La cotización venció. Buscá vuelos de nuevo para ver el precio actual.";
            return response;
        }

        FlightOffer refreshed;
        try
        {
            refreshed = await flightProvider.RefreshOfferAsync(quote.ProviderOfferId, ct);
        }
        catch (FlightOfferExpiredException)
        {
            quote.Status = FlightQuoteStatus.UNAVAILABLE;
            quote.RevalidatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            response.Outcome = "UNAVAILABLE";
            response.Message = "Ese vuelo ya no está disponible. Buscá de nuevo para ver las opciones vigentes.";
            return response;
        }

        quote.RevalidatedAt = DateTimeOffset.UtcNow;
        quote.ExpiresAt = refreshed.ExpiresAt;
        response.ExpiresAt = refreshed.ExpiresAt;
        response.CurrentPrice = new MoneyResponse { Amount = refreshed.Price.Amount, Currency = refreshed.Price.Currency };
        response.CombinedTotal = CombineTotals(quote.Package!, refreshed.Price.Amount, refreshed.Price.Currency);

        if (refreshed.Price.DiffersFrom(new FlightPrice(quote.TotalAmount, quote.Currency)))
        {
            // El precio nuevo se guarda, pero InitialAmount queda intacto: es la memoria de lo que se
            // le mostró a la persona, y es lo que hace comparable la diferencia.
            quote.TotalAmount = refreshed.Price.Amount;
            quote.Currency = refreshed.Price.Currency;
            quote.Status = FlightQuoteStatus.PRICE_CHANGED;

            response.Outcome = "PRICE_CHANGED";
            response.RequiresAcceptance = true;
            response.Message = refreshed.Price.Amount > previous.Amount
                ? "El precio del vuelo subió desde que lo cotizaste. Revisá el nuevo total antes de continuar."
                : "El precio del vuelo bajó desde que lo cotizaste.";
        }
        else
        {
            quote.Status = FlightQuoteStatus.CONFIRMED;
            response.Outcome = "UNCHANGED";
            response.Message = "El precio del vuelo sigue vigente.";
        }

        await db.SaveChangesAsync(ct);
        return response;
    }

    // ---------------------------------------------------------------- mapeo

    private static PackageFlightRuleResponse ToResponse(PackageFlightRule rule) => new()
    {
        PackageId = rule.PackageId,
        DestinationIata = rule.DestinationIata,
        DestinationLabel = AirportCatalog.Describe(rule.DestinationIata),
        AllowedOrigins = [.. rule.Origins().Select(o => new AirportResponse { Iata = o, Label = AirportCatalog.Describe(o) })],
        CabinClass = rule.CabinClass.ToString(),
        OutboundOffsetDays = rule.OutboundOffsetDays,
        InboundOffsetDays = rule.InboundOffsetDays,
        RoundTrip = rule.RoundTrip,
    };

    private FlightQuoteOptionResponse ToOption(FlightQuote quote, FlightOffer offer, Package package) => new()
    {
        QuoteId = quote.Id,
        FlightPrice = new MoneyResponse { Amount = offer.Price.Amount, Currency = offer.Price.Currency },
        CombinedTotal = CombineTotals(package, offer.Price.Amount, offer.Price.Currency),
        CarrierName = offer.OwnerName,
        CarrierIata = offer.OwnerIataCode,
        ExpiresAt = offer.ExpiresAt,
        Slices =
        [
            .. offer.Slices.Select(slice => new FlightSliceResponse
            {
                OriginIata = slice.OriginIata,
                DestinationIata = slice.DestinationIata,
                DurationMinutes = slice.Duration is { } duration ? (int)duration.TotalMinutes : null,
                Stops = Math.Max(slice.Segments.Count - 1, 0),
                Segments =
                [
                    .. slice.Segments.Select(segment => new FlightSegmentResponse
                    {
                        OriginIata = segment.OriginIata,
                        DestinationIata = segment.DestinationIata,
                        DepartingAt = segment.DepartingAt,
                        ArrivingAt = segment.ArrivingAt,
                        CarrierIata = segment.MarketingCarrierIata,
                        CarrierName = segment.MarketingCarrierName,
                        FlightNumber = segment.FlightNumber,
                        CheckedBags = segment.CheckedBags,
                    }),
                ],
            }),
        ],
    };

    /// <summary>
    /// Paquete + vuelo **sólo si comparten moneda**. Si no, devuelve null y la pantalla muestra los dos
    /// importes por separado: no existe un tipo de cambio en este sistema y no se va a inventar uno.
    /// </summary>
    private static MoneyResponse? CombineTotals(Package package, decimal flightAmount, string flightCurrency) =>
        string.Equals(package.Currency, flightCurrency, StringComparison.OrdinalIgnoreCase)
            ? new MoneyResponse { Amount = package.Price + flightAmount, Currency = package.Currency }
            : null;

    private static string Summarize(FlightOffer offer)
    {
        var parts = offer.Slices.Select(slice =>
        {
            var first = slice.Segments.FirstOrDefault();
            var stops = Math.Max(slice.Segments.Count - 1, 0);
            var stopsLabel = stops == 0 ? "directo" : stops == 1 ? "1 escala" : $"{stops} escalas";
            return $"{slice.OriginIata}→{slice.DestinationIata} {first?.DepartingAt:dd/MM HH:mm} ({stopsLabel})";
        });

        var summary = $"{offer.OwnerName ?? offer.OwnerIataCode}: {string.Join(" · ", parts)}";
        return summary.Length > 500 ? summary[..500] : summary;
    }
}
