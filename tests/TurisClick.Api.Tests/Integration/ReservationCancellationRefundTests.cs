using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Experiences.Dtos;
using TurisClick.Api.Modules.Flights.Dtos;
using TurisClick.Api.Modules.Flights.Entities;
using TurisClick.Api.Modules.Packages.Dtos;
using TurisClick.Api.Modules.Reservations.Dtos;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Modules.Reservations.Payments;
using TurisClick.Api.Modules.Reservations.Services;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// Cancelación con reembolso de una reserva ya confirmada, contra PostgreSQL real y con el proveedor aéreo
/// FALSO: **ningún test llama a Duffel**.
///
/// Lo que se protege acá es plata y confianza:
/// el reembolso del paquete sale de la política que la reserva congeló —no de la que el operador tenga hoy—,
/// el del pasaje sale de lo que informa la aerolínea, los dos no se mezclan ni se suman entre monedas
/// distintas, el cupo se devuelve exactamente una vez, el libro de pagos no se reescribe nunca, y una
/// cancelación que queda a medias se dice a medias en vez de anunciarse como completa.
/// </summary>
[Collection(ApiCollection.Name)]
public class ReservationCancellationRefundTests(TurisClickApiFactory factory)
{
    private readonly TurisClickApiFactory _factory = factory;

    private const string StandardPolicy = "30:100;15:50;0:0";

    private sealed record Scenario(
        HttpClient Provider,
        HttpClient Tourist,
        Guid PackageId,
        Guid AvailabilityId,
        Guid ReservationId,
        decimal PackagePrice,
        string Currency);

    private static object[] PolicyTiers(params (int Days, int Percentage)[] tiers) =>
        [.. tiers.Select(t => new { minDaysBefore = t.Days, refundPercentage = t.Percentage })];

    /// <summary>La política estándar del enunciado: 30+ días 100%, 15+ 50%, menos no reembolsable.</summary>
    private static object[] StandardTiers() => PolicyTiers((30, 100), (15, 50), (0, 0));

    private async Task<T> QueryDbAsync<T>(Func<TurisClickDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TurisClickDbContext>());
    }

    private async Task<int> ReservedSlotsAsync(Guid availabilityId) =>
        await QueryDbAsync(db => db.PackageAvailabilities
            .AsNoTracking().Where(a => a.Id == availabilityId).Select(a => a.ReservedSlots).FirstAsync());

    private async Task<List<PaymentTransaction>> LedgerAsync(Guid reservationId) =>
        await QueryDbAsync(db => db.PaymentTransactions
            .AsNoTracking().Where(p => p.ReservationId == reservationId).OrderBy(p => p.CreatedAt).ToListAsync());

    private async Task<ReservationCancellation> CancellationAsync(Guid reservationId) =>
        await QueryDbAsync(db => db.ReservationCancellations
            .AsNoTracking().Include(c => c.Lines)
            .Where(c => c.ReservationId == reservationId)
            .OrderByDescending(c => c.CreatedAt).FirstAsync());

    private async Task<Guid> CreateCityAsync()
    {
        var admin = _factory.CreateClient();
        UseBearerToken(admin, await LoginAsAdminAsync(admin));
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var country = await (await admin.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"PaisC-{suffix}", type = "COUNTRY" })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await admin.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"RegionC-{suffix}", type = "REGION", parentId = country!.Id })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var city = await (await admin.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"CiudadC-{suffix}", type = "CITY", parentId = region!.Id })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);

        return city!.Id;
    }

    /// <summary>
    /// Un paquete publicado con política, una salida a `daysAhead` días y una reserva ya pagada. Si se pasa
    /// `flightOrigin`, el paquete incluye vuelo y la reserva lo emite.
    /// </summary>
    private async Task<Scenario> SeedConfirmedAsync(
        string prefix,
        object[]? policy = null,
        int daysAhead = 45,
        string currency = "USD",
        decimal price = 1090m,
        string? flightOrigin = null,
        int slots = 10,
        HttpClient? touristClient = null)
    {
        var cityId = await CreateCityAsync();

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), prefix);
        UseBearerToken(providerClient, provider.AccessToken);

        var package = await (await providerClient.PostAsJsonAsync("/api/packages", new
        {
            title = $"Paquete {prefix}-{Guid.NewGuid():N}",
            description = "Paquete de prueba con guía local, traslados y alojamiento incluidos.",
            destinationId = cityId,
            categoryIds = Array.Empty<Guid>(),
            durationDays = 4,
            price,
            currency,
            items = new[] { new { dayNumber = 1, sortOrder = 1, kind = "DESCRIPTIVE", title = "Llegada" } },
            images = Array.Empty<object>(),
            cancellationPolicy = policy,
        })).Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        var availability = await (await providerClient.PostAsJsonAsync($"/api/packages/{package!.Id}/availability", new
        {
            departureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(daysAhead).ToString("yyyy-MM-dd"),
            totalSlots = slots,
        })).Content.ReadFromJsonAsync<PackageAvailabilityResponse>(JsonOptions);

        await providerClient.PostAsync($"/api/packages/{package.Id}/publish", null);

        if (flightOrigin is not null)
            await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule", new
            {
                destinationIata = "LPB",
                allowedOriginIatas = new[] { flightOrigin },
                cabinClass = "ECONOMY",
                outboundOffsetDays = 0,
                inboundOffsetDays = 0,
                roundTrip = true,
            });

        var tourist = touristClient ?? _factory.CreateClient();
        if (touristClient is null) UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, $"{prefix}-t"));

        Guid? quoteId = null;
        if (flightOrigin is not null)
        {
            var quote = await (await tourist.PostAsJsonAsync($"/api/packages/{package.Id}/flight-quotes",
                new { originIata = flightOrigin, packageAvailabilityId = availability!.Id, travelers = 1 }))
                .Content.ReadFromJsonAsync<PackageFlightQuoteResponse>(JsonOptions);

            quoteId = quote!.Options[0].QuoteId;
        }

        var reservation = await (await tourist.PostAsJsonAsync("/api/reservations", new
        {
            packageAvailabilityId = availability!.Id,
            travelers = 1,
            flightQuoteId = quoteId,
        })).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        var payBody = flightOrigin is null
            ? (object)new { success = true, acceptPriceChanges = true }
            : new
            {
                success = true,
                acceptPriceChanges = true,
                travelers = new[]
                {
                    new
                    {
                        givenName = "Ana",
                        familyName = "Quiroga",
                        bornOn = "1990-05-14",
                        gender = "f",
                        title = "ms",
                        email = "ana.quiroga@example.test",
                        phoneNumber = "+59170000000",
                    },
                },
            };

        var paid = await (await tourist.PostAsJsonAsync($"/api/reservations/{reservation!.Id}/pay", payBody))
            .Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        Assert.Equal("CONFIRMED", paid!.Status);

        return new Scenario(providerClient, tourist, package.Id, availability.Id, reservation.Id, price, currency);
    }

    private static async Task<CancellationQuoteResponse> QuoteCancellationAsync(Scenario scenario)
    {
        var response = await scenario.Tourist.PostAsync($"/api/reservations/{scenario.ReservationId}/cancellation-quote", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CancellationQuoteResponse>(JsonOptions))!;
    }

    private static Task<HttpResponseMessage> CancelAsync(Scenario scenario, Guid? quoteId) =>
        scenario.Tourist.PostAsJsonAsync(
            $"/api/reservations/{scenario.ReservationId}/cancel", new { cancellationQuoteId = quoteId });

    // ================================================================ política y presupuesto

    [Fact]
    public async Task ConLaAnticipacionSuficienteSeDevuelveTodo()
    {
        var scenario = await SeedConfirmedAsync("cx-full", StandardTiers(), daysAhead: 45);

        var quote = await QuoteCancellationAsync(scenario);

        var line = Assert.Single(quote.Lines);
        Assert.Equal("PACKAGE", line.Component);
        Assert.Equal(1090m, line.PaidAmount);
        Assert.Equal(1090m, line.RefundAmount);
        Assert.Equal(0m, line.FeeAmount);
        Assert.Equal(100, line.RefundPercentage);
        Assert.True(line.RefundKnown);

        Assert.Equal(1090m, Assert.Single(quote.Refunds).Amount);
        Assert.Empty(quote.Fees);
    }

    [Fact]
    public async Task EnElTramoIntermedioSeDevuelveLaMitadYSeDiceElPorcentaje()
    {
        var scenario = await SeedConfirmedAsync("cx-half", StandardTiers(), daysAhead: 20);

        var quote = await QuoteCancellationAsync(scenario);

        var line = Assert.Single(quote.Lines);
        Assert.Equal(50, line.RefundPercentage);
        Assert.Equal(545m, line.RefundAmount);
        Assert.Equal(545m, line.FeeAmount);
        Assert.Contains("50%", line.Explanation);
    }

    [Fact]
    public async Task CercaDeLaSalidaNoHayReembolsoYSeDiceAsi()
    {
        var scenario = await SeedConfirmedAsync("cx-none", StandardTiers(), daysAhead: 5);

        var quote = await QuoteCancellationAsync(scenario);

        var line = Assert.Single(quote.Lines);
        Assert.Equal(0, line.RefundPercentage);
        Assert.Equal(0m, line.RefundAmount);
        Assert.Equal(1090m, line.FeeAmount);
        Assert.Empty(quote.Refunds);
        // La frase se reescribió en la Oleada 14: "no reembolsable según la política del operador" era
        // correcto pero sonaba a contrato. Lo que el test fija es que diga que no se devuelve nada.
        Assert.Contains("no devuelve nada", line.Explanation);
        Assert.Contains("no tiene reembolso", quote.Summary);
    }

    [Fact]
    public async Task SinPoliticaDelOperadorNoSeCancelaDesdeLaApp()
    {
        // Es la frontera honesta: sin política configurada no hay nada que aplicar, y suponerle un reembolso
        // a un operador que no lo ofreció sería inventarle una obligación comercial.
        var scenario = await SeedConfirmedAsync("cx-nopolicy", policy: null);

        var response = await scenario.Tourist.PostAsync(
            $"/api/reservations/{scenario.ReservationId}/cancellation-quote", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("CANCELLATION_POLICY_MISSING", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnaPoliticaContradictoriaNoSeGuarda()
    {
        var cityId = await CreateCityAsync();
        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), "cx-badpolicy");
        UseBearerToken(providerClient, provider.AccessToken);

        // Devolver más cuanto más cerca de la salida siempre es un error de carga.
        var response = await providerClient.PostAsJsonAsync("/api/packages", new
        {
            title = $"Paquete contradictorio-{Guid.NewGuid():N}",
            description = "Paquete de prueba con una política que se contradice a sí misma.",
            destinationId = cityId,
            categoryIds = Array.Empty<Guid>(),
            durationDays = 3,
            price = 100m,
            currency = "USD",
            items = new[] { new { dayNumber = 1, sortOrder = 1, kind = "DESCRIPTIVE", title = "Llegada" } },
            images = Array.Empty<object>(),
            cancellationPolicy = PolicyTiers((30, 50), (10, 100)),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CambiarLaPoliticaDelPaqueteNoCambiaLoQueYaSeVendio()
    {
        // El invariante que vuelve esto un contrato: lo que la persona aceptó al comprar no se puede editar
        // después desde el panel del operador.
        var scenario = await SeedConfirmedAsync("cx-snapshot", StandardTiers(), daysAhead: 45);

        var package = await (await scenario.Provider.GetAsync($"/api/packages/{scenario.PackageId}")).Content
            .ReadFromJsonAsync<PackageResponse>(JsonOptions);

        var update = await scenario.Provider.PutAsJsonAsync($"/api/packages/{scenario.PackageId}", new
        {
            package!.Title,
            package.Description,
            destinationId = package.DestinationId,
            categoryIds = Array.Empty<Guid>(),
            durationDays = package.DurationDays,
            package.Price,
            package.Currency,
            items = new[] { new { dayNumber = 1, sortOrder = 1, kind = "DESCRIPTIVE", title = "Llegada" } },
            images = Array.Empty<object>(),
            // Ahora el operador dice "nada es reembolsable".
            cancellationPolicy = PolicyTiers((0, 0)),
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var quote = await QuoteCancellationAsync(scenario);

        // La reserva vieja conserva su política: 100%.
        Assert.Equal(100, Assert.Single(quote.Lines).RefundPercentage);
        Assert.Equal(StandardPolicy, (await CancellationAsync(scenario.ReservationId)).Lines.First().PolicyApplied);
    }

    // ================================================================ ejecución

    [Fact]
    public async Task CancelarDevuelveElCupoUnaVezYAsientaElReembolso()
    {
        var scenario = await SeedConfirmedAsync("cx-exec", StandardTiers(), daysAhead: 45);
        Assert.Equal(1, await ReservedSlotsAsync(scenario.AvailabilityId));

        var quote = await QuoteCancellationAsync(scenario);
        var response = await CancelAsync(scenario, quote.QuoteId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var reservation = await response.Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        Assert.Equal("CANCELLED", reservation!.Status);
        Assert.Equal("COMPLETED", reservation.Cancellation!.Status);

        Assert.Equal(0, await ReservedSlotsAsync(scenario.AvailabilityId));

        // El libro conserva el cobro Y el reembolso: ninguna fila se reescribió.
        var ledger = await LedgerAsync(scenario.ReservationId);
        var charge = Assert.Single(ledger, t => t.Type == PaymentTransactionType.CHARGE);
        var refund = Assert.Single(ledger, t => t.Type == PaymentTransactionType.REFUND);

        Assert.Equal(PaymentTransactionStatus.SUCCEEDED, charge.Status);
        Assert.Equal(1090m, charge.Amount);
        Assert.Equal(1090m, refund.Amount);
        Assert.Equal(PaymentComponent.PACKAGE, refund.Component);
        Assert.Equal(scenario.Currency, refund.Currency);
    }

    [Fact]
    public async Task ElHistorialDelLibroNoSeReescribeNunca()
    {
        var scenario = await SeedConfirmedAsync("cx-immutable", StandardTiers(), daysAhead: 20);

        var before = await LedgerAsync(scenario.ReservationId);
        var chargeId = Assert.Single(before).Id;

        var quote = await QuoteCancellationAsync(scenario);
        await CancelAsync(scenario, quote.QuoteId);

        var after = await LedgerAsync(scenario.ReservationId);

        // La fila del cobro sigue existiendo, con su importe original: el reembolso se suma, no reemplaza.
        var charge = Assert.Single(after, t => t.Id == chargeId);
        Assert.Equal(PaymentTransactionType.CHARGE, charge.Type);
        Assert.Equal(1090m, charge.Amount);
        Assert.Equal(545m, Assert.Single(after, t => t.Type == PaymentTransactionType.REFUND).Amount);
        Assert.Equal(2, after.Count);
    }

    [Fact]
    public async Task CancelarSinPresupuestoAceptadoNoHaceNada()
    {
        var scenario = await SeedConfirmedAsync("cx-noquote", StandardTiers());

        var response = await CancelAsync(scenario, quoteId: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("CANCELLATION_QUOTE_REQUIRED", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, await ReservedSlotsAsync(scenario.AvailabilityId));
    }

    [Fact]
    public async Task UnPresupuestoVencidoNoSePuedeEjecutar()
    {
        var scenario = await SeedConfirmedAsync("cx-expired", StandardTiers());
        var quote = await QuoteCancellationAsync(scenario);

        await QueryDbAsync(async db =>
            await db.ReservationCancellations
                .Where(c => c.Id == quote.QuoteId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));

        var response = await CancelAsync(scenario, quote.QuoteId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("CANCELLATION_QUOTE_EXPIRED", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, await ReservedSlotsAsync(scenario.AvailabilityId));
    }

    [Fact]
    public async Task PedirUnPresupuestoNuevoInvalidaElAnterior()
    {
        // No es un capricho: la aerolínea sólo permite confirmar la última cancelación creada para una orden,
        // así que dejar dos "vigentes" sería ofrecer uno que ya no se puede ejecutar.
        var scenario = await SeedConfirmedAsync("cx-stale", StandardTiers());

        var first = await QuoteCancellationAsync(scenario);
        var second = await QuoteCancellationAsync(scenario);
        Assert.NotEqual(first.QuoteId, second.QuoteId);

        var response = await CancelAsync(scenario, first.QuoteId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("CANCELLATION_QUOTE_INVALID", await response.Content.ReadAsStringAsync());

        // El nuevo sí se puede ejecutar.
        Assert.Equal(HttpStatusCode.OK, (await CancelAsync(scenario, second.QuoteId)).StatusCode);
    }

    [Fact]
    public async Task CancelarDosVecesNoDevuelveElCupoDosVeces()
    {
        var scenario = await SeedConfirmedAsync("cx-dup", StandardTiers(), slots: 5);
        var quote = await QuoteCancellationAsync(scenario);

        Assert.Equal(HttpStatusCode.OK, (await CancelAsync(scenario, quote.QuoteId)).StatusCode);
        var second = await CancelAsync(scenario, quote.QuoteId);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(0, await ReservedSlotsAsync(scenario.AvailabilityId));
        Assert.Single(await LedgerAsync(scenario.ReservationId), t => t.Type == PaymentTransactionType.REFUND);
    }

    [Fact]
    public async Task DosCancelacionesSimultaneasSoloEjecutanUna()
    {
        var scenario = await SeedConfirmedAsync("cx-race", StandardTiers());
        var quote = await QuoteCancellationAsync(scenario);

        var responses = await Task.WhenAll(CancelAsync(scenario, quote.QuoteId), CancelAsync(scenario, quote.QuoteId));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(0, await ReservedSlotsAsync(scenario.AvailabilityId));
        Assert.Single(await LedgerAsync(scenario.ReservationId), t => t.Type == PaymentTransactionType.REFUND);
    }

    [Fact]
    public async Task LaReservaDeOtraPersonaNoSePuedePresupuestarNiCancelar()
    {
        var scenario = await SeedConfirmedAsync("cx-owner", StandardTiers());

        var intruder = _factory.CreateClient();
        UseBearerToken(intruder, await RegisterAndLoginTouristAsync(intruder, "cx-owner-b"));

        var quoteResponse = await intruder.PostAsync(
            $"/api/reservations/{scenario.ReservationId}/cancellation-quote", null);
        Assert.Equal(HttpStatusCode.Forbidden, quoteResponse.StatusCode);

        var cancelResponse = await intruder.PostAsJsonAsync(
            $"/api/reservations/{scenario.ReservationId}/cancel", new { cancellationQuoteId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Forbidden, cancelResponse.StatusCode);

        Assert.Equal(1, await ReservedSlotsAsync(scenario.AvailabilityId));
    }

    // ================================================================ vuelo

    [Fact]
    public async Task ConVueloSeCalculaPorComponenteYNoSeMezclanLasPoliticas()
    {
        var scenario = await SeedConfirmedAsync("cx-flight", StandardTiers(), daysAhead: 20, flightOrigin: "VVI");

        var quote = await QuoteCancellationAsync(scenario);

        Assert.Equal(2, quote.Lines.Count);

        var package = Assert.Single(quote.Lines, l => l.Component == "PACKAGE");
        Assert.Equal(50, package.RefundPercentage);
        Assert.Equal(545m, package.RefundAmount);

        var flight = Assert.Single(quote.Lines, l => l.Component == "FLIGHT");
        // La aerolínea devuelve lo que ella informa; la política del 50% del operador NO se le aplica.
        Assert.Null(flight.RefundPercentage);
        Assert.Equal(100m, flight.RefundAmount);
        Assert.Equal(flight.PaidAmount - 100m, flight.FeeAmount);
        Assert.Contains("aerolínea", flight.Explanation);

        // Misma moneda: un solo total, que es la suma de los dos reembolsos.
        var refund = Assert.Single(quote.Refunds);
        Assert.Equal(645m, refund.Amount);
        Assert.Equal("USD", refund.Currency);
    }

    [Fact]
    public async Task CancelarUnPaqueteConVueloCancelaElPasajeYReembolsaCadaParte()
    {
        var scenario = await SeedConfirmedAsync("cx-flight-exec", StandardTiers(), daysAhead: 45, flightOrigin: "VVI");

        var quote = await QuoteCancellationAsync(scenario);
        var response = await CancelAsync(scenario, quote.QuoteId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var reservation = await response.Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        Assert.Equal("CANCELLED", reservation!.Status);
        Assert.Equal("COMPLETED", reservation.Cancellation!.Status);
        Assert.True(reservation.Cancellation.FlightCancelled);
        Assert.Equal("CANCELLED", reservation.Flight!.Status);

        Assert.Equal(0, await ReservedSlotsAsync(scenario.AvailabilityId));

        // Dos reembolsos, uno por componente: el del paquete y el del pasaje son eventos distintos.
        var refunds = (await LedgerAsync(scenario.ReservationId))
            .Where(t => t.Type == PaymentTransactionType.REFUND)
            .ToList();

        Assert.Equal(2, refunds.Count);
        Assert.Contains(refunds, r => r.Component == PaymentComponent.PACKAGE);
        Assert.Contains(refunds, r => r.Component == PaymentComponent.FLIGHT);
    }

    [Fact]
    public async Task UnPasajeNoReembolsableSeInformaComoNoReembolsable()
    {
        var scenario = await SeedConfirmedAsync("cx-nrf", StandardTiers(), daysAhead: 45, flightOrigin: "NRF");

        var quote = await QuoteCancellationAsync(scenario);

        var flight = Assert.Single(quote.Lines, l => l.Component == "FLIGHT");
        Assert.Equal(0m, flight.RefundAmount);
        Assert.True(flight.RefundKnown);
        Assert.Contains("no devuelve nada", flight.Explanation);

        // El paquete sí se devuelve: la política del operador no depende de la de la aerolínea.
        Assert.Equal(1090m, Assert.Single(quote.Lines, l => l.Component == "PACKAGE").RefundAmount);
        Assert.Equal(1090m, Assert.Single(quote.Refunds).Amount);
    }

    [Fact]
    public async Task SiLaAerolineaNoInformaCuantoDevuelveNoSePrometeNada()
    {
        // "No informado" no es "cero": decirle a alguien que su pasaje es reembolsable sin que la aerolínea lo
        // haya confirmado sería prometer plata ajena.
        var scenario = await SeedConfirmedAsync("cx-unknown", StandardTiers(), daysAhead: 45, flightOrigin: "NIR");

        var quote = await QuoteCancellationAsync(scenario);

        var flight = Assert.Single(quote.Lines, l => l.Component == "FLIGHT");
        Assert.False(flight.RefundKnown);
        Assert.Equal(0m, flight.RefundAmount);
        Assert.Contains("no informó", flight.Explanation);
        Assert.True(quote.HasUnknownRefund);
        Assert.Contains("Falta lo que informe la aerolínea", quote.Summary);
    }

    [Fact]
    public async Task SiLaAerolineaRechazaLaCancelacionNoSeCancelaNada()
    {
        var scenario = await SeedConfirmedAsync("cx-noc", StandardTiers(), daysAhead: 45, flightOrigin: "NOC");

        var quote = await QuoteCancellationAsync(scenario);
        var response = await CancelAsync(scenario, quote.QuoteId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var reservation = await response.Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        // La reserva sigue vigente: no se liberó cupo, no se devolvió plata y no se canceló el pasaje.
        Assert.Equal("CONFIRMED", reservation!.Status);
        Assert.Equal("FAILED", reservation.Cancellation!.Status);
        Assert.False(reservation.Cancellation.FlightCancelled);
        Assert.Equal(1, await ReservedSlotsAsync(scenario.AvailabilityId));
        Assert.DoesNotContain(await LedgerAsync(scenario.ReservationId), t => t.Type == PaymentTransactionType.REFUND);
    }

    [Fact]
    public async Task ConMonedasDistintasNoSeInventaUnTotalUnico()
    {
        // Paquete en bolivianos, pasaje en dólares: dos reembolsos, nunca uno convertido.
        var scenario = await SeedConfirmedAsync(
            "cx-fx", StandardTiers(), daysAhead: 45, currency: "BOB", flightOrigin: "VVI");

        var quote = await QuoteCancellationAsync(scenario);

        Assert.Equal(2, quote.Refunds.Count);
        Assert.Contains(quote.Refunds, r => r.Currency == "BOB");
        Assert.Contains(quote.Refunds, r => r.Currency == "USD");
        Assert.Contains("+", quote.Summary);

        await CancelAsync(scenario, quote.QuoteId);

        var refunds = (await LedgerAsync(scenario.ReservationId))
            .Where(t => t.Type == PaymentTransactionType.REFUND)
            .ToList();

        Assert.Equal(2, refunds.Count);
        Assert.Equal(["BOB", "USD"], refunds.Select(r => r.Currency).OrderBy(c => c));
    }

    // ================================================================ fallas y resolución

    [Fact]
    public async Task SiElReembolsoFallaLaCancelacionQuedaPendienteYNoSeMiente()
    {
        var scenario = await SeedConfirmedAsync("cx-refundfail", StandardTiers(), daysAhead: 45);
        var quote = await QuoteCancellationAsync(scenario);

        // Una pasarela que rechaza los reembolsos, registrada sólo para este test: ejercita el camino en el
        // que lo cancelado queda cancelado pero la plata no vuelve, sin ensuciar el gateway real con valores
        // mágicos.
        using var failing = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddScoped<IPaymentGateway, RejectingRefundGateway>()));

        var client = failing.CreateClient();
        client.DefaultRequestHeaders.Authorization = scenario.Tourist.DefaultRequestHeaders.Authorization;

        var response = await client.PostAsJsonAsync(
            $"/api/reservations/{scenario.ReservationId}/cancel", new { cancellationQuoteId = quote.QuoteId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cancellation = await CancellationAsync(scenario.ReservationId);
        Assert.Equal(ReservationCancellationStatus.REFUND_PENDING, cancellation.Status);

        // Lo cancelado está cancelado: el cupo volvió al catálogo.
        Assert.Equal(0, await ReservedSlotsAsync(scenario.AvailabilityId));

        // Y el intento fallido quedó asentado: es lo que permite saber después qué operación falló.
        var refund = Assert.Single(await LedgerAsync(scenario.ReservationId), t => t.Type == PaymentTransactionType.REFUND);
        Assert.Equal(PaymentTransactionStatus.FAILED, refund.Status);
    }

    [Fact]
    public async Task LaResolucionReintentaElReembolsoPendienteYCompleta()
    {
        var scenario = await SeedConfirmedAsync("cx-resolve", StandardTiers(), daysAhead: 45);
        var quote = await QuoteCancellationAsync(scenario);
        await CancelAsync(scenario, quote.QuoteId);

        // Se fuerza el estado pendiente como si la pasarela hubiera fallado, y se borra el asiento exitoso
        // para que el reintento tenga algo que hacer.
        await QueryDbAsync(async db =>
        {
            await db.PaymentTransactions
                .Where(p => p.ReservationId == scenario.ReservationId && p.Type == PaymentTransactionType.REFUND)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentTransactionStatus.FAILED));

            return await db.ReservationCancellations
                .Where(c => c.Id == quote.QuoteId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, ReservationCancellationStatus.REFUND_PENDING)
                    .SetProperty(c => c.CompletedAt, (DateTimeOffset?)null));
        });

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReservationCancellationService>();
        var status = await service.ResolveAsync(quote.QuoteId, CancellationToken.None);

        Assert.Equal(ReservationCancellationStatus.COMPLETED, status);
        Assert.Equal(0, await ReservedSlotsAsync(scenario.AvailabilityId));
    }

    [Fact]
    public async Task UnaCancelacionAMediasSeResuelvePreguntandoAlProveedor()
    {
        // CNX: consultar la orden la muestra ya cancelada. Es el caso en que la confirmación salió, la
        // respuesta no llegó, y la resolución tiene que PREGUNTAR antes de reintentar.
        var scenario = await SeedConfirmedAsync("cx-midway", StandardTiers(), daysAhead: 45, flightOrigin: "CNX");
        var quote = await QuoteCancellationAsync(scenario);

        await QueryDbAsync(async db =>
        {
            await db.Reservations
                .Where(r => r.Id == scenario.ReservationId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReservationStatus.CANCELLING));

            return await db.ReservationCancellations
                .Where(c => c.Id == quote.QuoteId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, ReservationCancellationStatus.REQUIRES_REVIEW)
                    .SetProperty(c => c.AcceptedAt, DateTimeOffset.UtcNow));
        });

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReservationCancellationService>();
        var status = await service.ResolveAsync(quote.QuoteId, CancellationToken.None);

        Assert.True(status is ReservationCancellationStatus.COMPLETED or ReservationCancellationStatus.REFUND_PENDING);
        Assert.Equal(0, await ReservedSlotsAsync(scenario.AvailabilityId));

        var reservation = await QueryDbAsync(db => db.Reservations
            .AsNoTracking().FirstAsync(r => r.Id == scenario.ReservationId));
        Assert.Equal(ReservationStatus.CANCELLED, reservation.Status);
    }

    // ================================================================ visibilidad y regresión

    [Fact]
    public async Task ElAdminVeElLibroCompletoYLasCancelaciones()
    {
        var scenario = await SeedConfirmedAsync("cx-admin", StandardTiers(), daysAhead: 20);
        var quote = await QuoteCancellationAsync(scenario);
        await CancelAsync(scenario, quote.QuoteId);

        var admin = _factory.CreateClient();
        UseBearerToken(admin, await LoginAsAdminAsync(admin));

        var response = await admin.GetAsync($"/api/admin/reservations/{scenario.ReservationId}/payments");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payments = await response.Content.ReadFromJsonAsync<ReservationPaymentsResponse>(JsonOptions);

        var balance = Assert.Single(payments!.Balances);
        Assert.Equal(1090m, balance.Charged);
        Assert.Equal(545m, balance.Refunded);
        Assert.Equal(545m, balance.Net);

        Assert.Equal(2, payments.Transactions.Count);
        Assert.Equal("COMPLETED", Assert.Single(payments.Cancellations).Status);
    }

    /// <summary>
    /// El proceso de fondo vive dentro de la API, y en un App Service gratuito la aplicación se duerme sin
    /// tráfico: mientras duerme no reintenta nada. Este endpoint es la salida manual para quien está
    /// operando, y tiene que ser idempotente —un reembolso ya hecho no se repite— porque alguien lo va a
    /// apretar dos veces.
    /// </summary>
    [Fact]
    public async Task ElAdminPuedeReintentarLasCancelacionesPendientesSinDuplicarReembolsos()
    {
        var scenario = await SeedConfirmedAsync("cx-resolve", StandardTiers(), daysAhead: 20);
        var quote = await QuoteCancellationAsync(scenario);
        await CancelAsync(scenario, quote.QuoteId);

        var admin = _factory.CreateClient();
        UseBearerToken(admin, await LoginAsAdminAsync(admin));

        var primera = await admin.PostAsync("/api/admin/cancellations/resolve", null);
        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);

        var segunda = await admin.PostAsync("/api/admin/cancellations/resolve", null);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);

        // Lo que importa no es cuántas resolvió —esta ya estaba completa— sino que llamarlo dos veces no
        // agregue un segundo reembolso al libro.
        var ledger = await LedgerAsync(scenario.ReservationId);
        Assert.Single(ledger.Where(t => t.Type == PaymentTransactionType.CHARGE));
        Assert.Single(ledger.Where(t => t.Type == PaymentTransactionType.REFUND
                                        && t.Status == PaymentTransactionStatus.SUCCEEDED));
    }

    [Fact]
    public async Task ReintentarLasCancelacionesPendientesEsSoloDelAdmin()
    {
        var scenario = await SeedConfirmedAsync("cx-resolve-guard", StandardTiers());

        var response = await scenario.Tourist.PostAsync("/api/admin/cancellations/resolve", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnTuristaNoPuedeVerElLibroDeNadie()
    {
        var scenario = await SeedConfirmedAsync("cx-admin-guard", StandardTiers());

        var response = await scenario.Tourist.GetAsync($"/api/admin/reservations/{scenario.ReservationId}/payments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ElOperadorVeLaPoliticaAplicadaYLoReembolsadoDeSuLinea()
    {
        var scenario = await SeedConfirmedAsync("cx-provider", StandardTiers(), daysAhead: 20);
        var quote = await QuoteCancellationAsync(scenario);
        await CancelAsync(scenario, quote.QuoteId);

        var received = await (await scenario.Provider.GetAsync("/api/companies/me/reservations")).Content.ReadAsStringAsync();

        // Ve que le cancelaron y con qué política; nada del medio de pago, que no existe en el libro.
        Assert.Contains(scenario.ReservationId.ToString(), received);
        Assert.Contains("cancellationPolicy", received);
        Assert.DoesNotContain("idempotencyKey", received);
        Assert.DoesNotContain("providerReference", received);
    }

    [Fact]
    public async Task UnaExperienciaConfirmadaSigueSinCancelarseDesdeLaApp()
    {
        // Regresión y frontera explícita: las experiencias todavía no tienen política configurable, así que
        // una vez confirmadas no se cancelan desde la app — igual que antes de esta oleada.
        var cityId = await CreateCityAsync();

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), "cx-exp");
        UseBearerToken(providerClient, provider.AccessToken);

        var experience = await (await providerClient.PostAsJsonAsync("/api/experiences", new
        {
            title = $"Tour {Guid.NewGuid():N}",
            description = "Descripción suficientemente larga para pasar la validación.",
            destinationId = cityId,
            categoryIds = Array.Empty<Guid>(),
            price = 40m,
            currency = "USD",
        })).Content.ReadFromJsonAsync<ExperienceResponse>(JsonOptions);

        var availability = await (await providerClient.PostAsJsonAsync($"/api/experiences/{experience!.Id}/availability", new
        {
            date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(40).ToString("yyyy-MM-dd"),
            startTime = "09:00:00",
            totalSlots = 10,
        })).Content.ReadFromJsonAsync<ExperienceAvailabilityResponse>(JsonOptions);

        await providerClient.PostAsync($"/api/experiences/{experience.Id}/publish", null);

        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "cx-exp-t"));

        var reservation = await (await tourist.PostAsJsonAsync("/api/reservations", new
        {
            experienceAvailabilityId = availability!.Id,
            travelers = 1,
        })).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        await tourist.PostAsJsonAsync($"/api/reservations/{reservation!.Id}/pay",
            new { success = true, acceptPriceChanges = true });

        var response = await tourist.PostAsync($"/api/reservations/{reservation.Id}/cancellation-quote", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("CANCELLATION_POLICY_MISSING", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnaReservaSinPagarSeSigueCancelandoGratisYSinPresupuesto()
    {
        // Regresión del camino que ya existía: sin pagar no hay plata que devolver, así que no hace falta
        // presupuesto ni queda ningún asiento en el libro.
        var cityId = await CreateCityAsync();

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), "cx-pending");
        UseBearerToken(providerClient, provider.AccessToken);

        var package = await (await providerClient.PostAsJsonAsync("/api/packages", new
        {
            title = $"Paquete pendiente-{Guid.NewGuid():N}",
            description = "Paquete de prueba con guía local, traslados y alojamiento incluidos.",
            destinationId = cityId,
            categoryIds = Array.Empty<Guid>(),
            durationDays = 3,
            price = 300m,
            currency = "USD",
            items = new[] { new { dayNumber = 1, sortOrder = 1, kind = "DESCRIPTIVE", title = "Llegada" } },
            images = Array.Empty<object>(),
        })).Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        var availability = await (await providerClient.PostAsJsonAsync($"/api/packages/{package!.Id}/availability", new
        {
            departureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30).ToString("yyyy-MM-dd"),
            totalSlots = 5,
        })).Content.ReadFromJsonAsync<PackageAvailabilityResponse>(JsonOptions);

        await providerClient.PostAsync($"/api/packages/{package.Id}/publish", null);

        var tourist = _factory.CreateClient();
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "cx-pending-t"));

        var reservation = await (await tourist.PostAsJsonAsync("/api/reservations", new
        {
            packageAvailabilityId = availability!.Id,
            travelers = 2,
        })).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        var response = await tourist.PostAsJsonAsync(
            $"/api/reservations/{reservation!.Id}/cancel", new { cancellationQuoteId = (Guid?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = await response.Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        Assert.Equal("CANCELLED", cancelled!.Status);
        Assert.Null(cancelled.Cancellation);

        Assert.Equal(0, await ReservedSlotsAsync(availability.Id));
        Assert.Empty(await LedgerAsync(reservation.Id));
    }

    // ================================================================ auxiliares

    /// <summary>
    /// Pasarela que aprueba cobros y rechaza reembolsos. No es un valor mágico escondido en el simulador: es
    /// una implementación aparte que sólo este test registra, para poder ejercitar el camino en el que la
    /// plata no vuelve sin ensuciar el gateway real.
    /// </summary>
    private sealed class RejectingRefundGateway : IPaymentGateway
    {
        public Task<PaymentChargeResult> ChargeAsync(PaymentChargeRequest request, CancellationToken ct) =>
            Task.FromResult(new PaymentChargeResult(request.SimulatedSuccess, request.SimulatedSuccess ? null : "Rechazado."));

        public Task VoidAsync(PaymentVoidRequest request, CancellationToken ct) => Task.CompletedTask;

        public Task<PaymentRefundResult> RefundAsync(PaymentRefundRequest request, CancellationToken ct) =>
            Task.FromResult(new PaymentRefundResult(false, "La pasarela rechazó el reembolso.", null));
    }
}
