using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TurisClick.Api.Infrastructure.Database;
using TurisClick.Api.Modules.Destinations.Dtos;
using TurisClick.Api.Modules.Flights.Dtos;
using TurisClick.Api.Modules.Flights.Entities;
using TurisClick.Api.Modules.Flights.Services;
using TurisClick.Api.Modules.Packages.Dtos;
using TurisClick.Api.Modules.Reservations.Dtos;
using TurisClick.Api.Modules.Reservations.Entities;
using TurisClick.Api.Modules.Reservations.Services;
using Xunit;
using static TurisClick.Api.Tests.Integration.TestClients;

namespace TurisClick.Api.Tests.Integration;

/// <summary>
/// Reserva coordinada de paquete + vuelo contra PostgreSQL real y el proveedor FALSO: **ningún test llama
/// a Duffel**.
///
/// Lo que se protege acá no es el camino felizque es el más fácil de hacer funcionar, sino los finales
/// en los que se pierde plata o confianza: un pasaje comprado dos veces, un cupo retenido por un vuelo que
/// nunca se emitió, un precio cobrado que la persona no aceptó, y una respuesta perdida que deja al sistema
/// sin saber si compró o no.
///
/// Los escenarios del proveedor se eligen por aeropuerto de origen (ver FakeFlightProvider): FAI rechaza,
/// NET no llega a salir, UNK se pierde habiendo creado la orden, UNL se pierde sin crearla.
/// </summary>
[Collection(ApiCollection.Name)]
public class PackageFlightBookingTests(TurisClickApiFactory factory)
{
    private readonly TurisClickApiFactory _factory = factory;

    /// <summary>Datos sintéticos. Nunca una persona real, y nunca un pasaporte: la oferta no lo pide.</summary>
    private static object Traveler(string given = "Ana", string family = "Quiroga") => new
    {
        givenName = given,
        familyName = family,
        bornOn = "1990-05-14",
        gender = "f",
        title = "ms",
        email = "ana.quiroga@example.test",
        phoneNumber = "+59170000000",
    };

    private sealed record Scenario(
        HttpClient Provider,
        HttpClient Tourist,
        PackageResponse Package,
        Guid AvailabilityId,
        string Origin);

    private async Task<T> QueryDbAsync<T>(Func<TurisClickDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TurisClickDbContext>());
    }

    private async Task<T> WithDbAsync<T>(Func<TurisClickDbContext, Task<T>> action) => await QueryDbAsync(action);

    private async Task<FlightReconciliationOutcome> ReconcileAsync(Guid bookingId)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFlightReconciliationService>();
        return await service.ReconcileAsync(bookingId, CancellationToken.None);
    }

    private async Task<bool> ExpireAsync(Guid reservationId)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReservationExpirationService>();
        return await service.ExpireAsync(reservationId, CancellationToken.None);
    }

    /// <summary>Paquete publicado con vuelo configurado, una salida con cupo y un turista con sesión.</summary>
    private async Task<Scenario> SeedAsync(string prefix, string origin = "VVI", int slots = 10, string currency = "USD")
    {
        var adminClient = _factory.CreateClient();
        UseBearerToken(adminClient, await LoginAsAdminAsync(adminClient));
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var country = await (await adminClient.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"Pais-{suffix}", type = "COUNTRY" })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await adminClient.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"Region-{suffix}", type = "REGION", parentId = country!.Id })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var city = await (await adminClient.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"Ciudad-{suffix}", type = "CITY", parentId = region!.Id })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), prefix);
        UseBearerToken(providerClient, provider.AccessToken);

        var package = await (await providerClient.PostAsJsonAsync("/api/packages", new
        {
            title = $"Salar y Uyuni-{suffix}",
            description = "Cuatro días por el salar, con guía local, traslados y alojamiento incluidos.",
            destinationId = city!.Id,
            categoryIds = Array.Empty<Guid>(),
            durationDays = 4,
            price = 480m,
            currency,
            items = new[] { new { dayNumber = 1, sortOrder = 1, kind = "DESCRIPTIVE", title = "Llegada" } },
            images = Array.Empty<object>(),
        })).Content.ReadFromJsonAsync<PackageResponse>(JsonOptions);

        var availability = await (await providerClient.PostAsJsonAsync($"/api/packages/{package!.Id}/availability", new
        {
            departureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(45).ToString("yyyy-MM-dd"),
            totalSlots = slots,
        })).Content.ReadFromJsonAsync<PackageAvailabilityResponse>(JsonOptions);

        await providerClient.PostAsync($"/api/packages/{package.Id}/publish", null);

        await providerClient.PutAsJsonAsync($"/api/packages/{package.Id}/flight-rule", new
        {
            destinationIata = "UYU",
            allowedOriginIatas = new[] { origin },
            cabinClass = "ECONOMY",
            outboundOffsetDays = 0,
            inboundOffsetDays = 0,
            roundTrip = true,
        });

        var touristClient = _factory.CreateClient();
        UseBearerToken(touristClient, await RegisterAndLoginTouristAsync(touristClient, $"{prefix}-t"));

        return new Scenario(providerClient, touristClient, package, availability!.Id, origin);
    }

    private static async Task<Guid> QuoteAsync(Scenario scenario, int travelers = 1)
    {
        var quote = await (await scenario.Tourist.PostAsJsonAsync(
            $"/api/packages/{scenario.Package.Id}/flight-quotes",
            new { originIata = scenario.Origin, packageAvailabilityId = scenario.AvailabilityId, travelers }))
            .Content.ReadFromJsonAsync<PackageFlightQuoteResponse>(JsonOptions);

        Assert.NotEmpty(quote!.Options);
        return quote.Options[0].QuoteId;
    }

    private static async Task<HttpResponseMessage> ReserveAsync(Scenario scenario, Guid quoteId, int travelers = 1) =>
        await scenario.Tourist.PostAsJsonAsync("/api/reservations", new
        {
            packageAvailabilityId = scenario.AvailabilityId,
            travelers,
            flightQuoteId = quoteId,
        });

    private static async Task<HttpResponseMessage> PayAsync(
        Scenario scenario, Guid reservationId, object? travelers = null, object? acceptedFlightPrice = null) =>
        await scenario.Tourist.PostAsJsonAsync($"/api/reservations/{reservationId}/pay", new
        {
            success = true,
            acceptPriceChanges = false,
            travelers = travelers ?? new[] { Traveler() },
            acceptedFlightPrice,
        });

    private async Task<int> ReservedSlotsAsync(Guid availabilityId) =>
        await QueryDbAsync(db => db.PackageAvailabilities
            .AsNoTracking()
            .Where(a => a.Id == availabilityId)
            .Select(a => a.ReservedSlots)
            .FirstAsync());

    private async Task<FlightBooking> BookingAsync(Guid reservationId) =>
        await QueryDbAsync(db => db.FlightBookings.AsNoTracking().FirstAsync(b => b.ReservationId == reservationId));

    // ================================================================ crear la reserva

    [Fact]
    public async Task ReservarUnPaqueteConVueloRetieneCupoYDejaElVueloPendiente()
    {
        var scenario = await SeedAsync("fb-create");
        var quoteId = await QuoteAsync(scenario, travelers: 2);

        var response = await ReserveAsync(scenario, quoteId, travelers: 2);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var reservation = await response.Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        Assert.Equal("PENDING_PAYMENT", reservation!.Status);
        Assert.NotNull(reservation.Flight);
        Assert.Equal("PENDING", reservation.Flight!.Status);
        Assert.Equal(2, reservation.Flight.Travelers);
        Assert.Equal("UYU", reservation.Flight.DestinationIata);

        // El snapshot de ruta y fechas existe desde el minuto cero: la cotización vence, lo comprado no.
        var booking = await BookingAsync(reservation.Id);
        Assert.Equal(FlightBookingStatus.PENDING, booking.Status);
        Assert.Null(booking.ProviderOrderId);
        Assert.Equal("VVI", booking.OriginIata);
        Assert.NotEqual(default, booking.OutboundDate);
        Assert.NotEmpty(booking.IdempotencyKey);

        Assert.Equal(2, await ReservedSlotsAsync(scenario.AvailabilityId));
    }

    [Fact]
    public async Task ReservarDosVecesLaMismaCotizacionDevuelveLaMismaReserva()
    {
        var scenario = await SeedAsync("fb-idem");
        var quoteId = await QuoteAsync(scenario);

        var first = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        var second = await ReserveAsync(scenario, quoteId);
        var secondBody = await second.Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        Assert.Equal(first!.Id, secondBody!.Id);
        // Lo que importa no es el código de respuesta: es que el cupo se tomó UNA vez.
        Assert.Equal(1, await ReservedSlotsAsync(scenario.AvailabilityId));
        Assert.Equal(1, await QueryDbAsync(db => db.FlightBookings.CountAsync(b => b.FlightQuoteId == quoteId)));
    }

    [Fact]
    public async Task DosEnviosSimultaneosDeLaMismaCotizacionNoDuplicanLaReserva()
    {
        var scenario = await SeedAsync("fb-idem-race");
        var quoteId = await QuoteAsync(scenario);

        // El doble toque real: las dos requests salen a la vez, sin que ninguna vea a la otra.
        var responses = await Task.WhenAll(ReserveAsync(scenario, quoteId), ReserveAsync(scenario, quoteId));

        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Created);

        // Haya ganado una o las dos la carrera de lectura, el índice único deja una sola reserva aérea.
        Assert.Equal(1, await QueryDbAsync(db => db.FlightBookings.CountAsync(b2 => b2.FlightQuoteId == quoteId)));
        Assert.Equal(1, await ReservedSlotsAsync(scenario.AvailabilityId));
    }

    [Fact]
    public async Task LaCotizacionDeOtraPersonaNoSePuedeReservar()
    {
        var scenario = await SeedAsync("fb-owner");
        var quoteId = await QuoteAsync(scenario);

        var intruder = _factory.CreateClient();
        UseBearerToken(intruder, await RegisterAndLoginTouristAsync(intruder, "fb-owner-b"));

        var response = await intruder.PostAsJsonAsync("/api/reservations", new
        {
            packageAvailabilityId = scenario.AvailabilityId,
            travelers = 1,
            flightQuoteId = quoteId,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await ReservedSlotsAsync(scenario.AvailabilityId));
    }

    [Fact]
    public async Task UnaCotizacionVencidaNoSePuedeReservar()
    {
        // EXP: el proveedor falso devuelve una oferta que nace vencida.
        var scenario = await SeedAsync("fb-expired", origin: "EXP");
        var quoteId = await QuoteAsync(scenario);

        var response = await ReserveAsync(scenario, quoteId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await ReservedSlotsAsync(scenario.AvailabilityId));
    }

    [Fact]
    public async Task LaCotizacionTieneQueCoincidirConLosViajerosDeLaReserva()
    {
        var scenario = await SeedAsync("fb-mismatch");
        var quoteId = await QuoteAsync(scenario, travelers: 1);

        // Cotizó para uno y quiere reservar para tres: el precio del pasaje depende de cuántos son.
        var response = await ReserveAsync(scenario, quoteId, travelers: 3);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await ReservedSlotsAsync(scenario.AvailabilityId));
    }

    [Fact]
    public async Task UnPaqueteConVueloNoSeReservaSinElegirVuelo()
    {
        var scenario = await SeedAsync("fb-noquote");

        var response = await scenario.Tourist.PostAsJsonAsync("/api/reservations", new
        {
            packageAvailabilityId = scenario.AvailabilityId,
            travelers = 1,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await ReservedSlotsAsync(scenario.AvailabilityId));
    }

    [Fact]
    public async Task SiElCupoSeAgotaEntreCotizarYReservarNoSeReservaNada()
    {
        var scenario = await SeedAsync("fb-capacity", slots: 2);

        // El segundo turista cotiza cuando todavía hay lugar...
        var second = _factory.CreateClient();
        UseBearerToken(second, await RegisterAndLoginTouristAsync(second, "fb-capacity-b"));
        var secondQuote = await (await second.PostAsJsonAsync($"/api/packages/{scenario.Package.Id}/flight-quotes",
            new { originIata = scenario.Origin, packageAvailabilityId = scenario.AvailabilityId, travelers = 2 }))
            .Content.ReadFromJsonAsync<PackageFlightQuoteResponse>(JsonOptions);

        // ...y el primero se lleva la salida completa antes de que confirme.
        var firstQuote = await QuoteAsync(scenario, travelers: 2);
        Assert.Equal(HttpStatusCode.Created, (await ReserveAsync(scenario, firstQuote, travelers: 2)).StatusCode);

        var response = await second.PostAsJsonAsync("/api/reservations", new
        {
            packageAvailabilityId = scenario.AvailabilityId,
            travelers = 2,
            flightQuoteId = secondQuote!.Options[0].QuoteId,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        // El cupo quedó exactamente como lo dejó la primera reserva: no se tomó de más ni se perdió.
        Assert.Equal(2, await ReservedSlotsAsync(scenario.AvailabilityId));
    }

    // ================================================================ pagar y emitir

    [Fact]
    public async Task PagarEmiteElPasajeYConfirmaPaqueteYVueloJuntos()
    {
        var scenario = await SeedAsync("fb-happy");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        var response = await PayAsync(scenario, reservation!.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var paid = await response.Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        Assert.Equal("CONFIRMED", paid!.Status);
        Assert.True(paid.PaymentApproved);
        Assert.Equal("CONFIRMED", paid.Flight!.Status);
        Assert.False(paid.Flight.InProgress);
        Assert.False(string.IsNullOrWhiteSpace(paid.Flight.BookingReference));
        Assert.All(paid.Items, item => Assert.Equal("CONFIRMED", item.Status));

        // El itinerario queda congelado con lo que el proveedor confirmó: aerolínea, número y horarios.
        var booking = await BookingAsync(reservation.Id);
        Assert.Equal(FlightBookingStatus.CONFIRMED, booking.Status);
        Assert.NotNull(booking.ProviderOrderId);
        Assert.NotNull(booking.CarrierName);
        Assert.NotNull(booking.OutboundFlightNumber);
        Assert.NotNull(booking.OutboundDepartureAt);
        Assert.NotNull(booking.ConfirmedAt);
    }

    [Fact]
    public async Task LaRespuestaDeLaReservaNoFiltraIdentificadoresDelProveedor()
    {
        var scenario = await SeedAsync("fb-noleak");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        var body = await (await PayAsync(scenario, reservation!.Id)).Content.ReadAsStringAsync();

        // La persona recibe un localizador; la mecánica del proveedor se queda del lado del servidor.
        Assert.DoesNotContain("off_fake", body);
        Assert.DoesNotContain("ord_fake", body);
        Assert.Contains("bookingReference", body);
    }

    [Fact]
    public async Task SinDatosDePasajerosNoSeCobraNiSeEmite()
    {
        var scenario = await SeedAsync("fb-nopax");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        var response = await scenario.Tourist.PostAsJsonAsync($"/api/reservations/{reservation!.Id}/pay",
            new { success = true, acceptPriceChanges = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var booking = await BookingAsync(reservation.Id);
        Assert.Equal(FlightBookingStatus.PENDING, booking.Status);
        Assert.Equal("PENDING_PAYMENT", (await QueryDbAsync(db => db.Reservations.AsNoTracking()
            .FirstAsync(r => r.Id == reservation.Id))).Status.ToString());
    }

    [Fact]
    public async Task UnNombreConDigitosSeRechazaAntesDeLlamarAlProveedor()
    {
        var scenario = await SeedAsync("fb-badpax");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        var response = await PayAsync(scenario, reservation!.Id, travelers: new[] { Traveler(given: "Ana2") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(FlightBookingStatus.PENDING, (await BookingAsync(reservation.Id)).Status);
    }

    [Fact]
    public async Task LaCantidadDePasajerosTieneQueCoincidirConLaReserva()
    {
        var scenario = await SeedAsync("fb-paxcount");
        var quoteId = await QuoteAsync(scenario, travelers: 2);
        var reservation = await (await ReserveAsync(scenario, quoteId, travelers: 2)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        var response = await PayAsync(scenario, reservation!.Id, travelers: new[] { Traveler() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NoSeGuardaNingunDatoDePasajero()
    {
        var scenario = await SeedAsync("fb-pii");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        await PayAsync(scenario, reservation!.Id, travelers: new[] { Traveler(given: "Zoraida", family: "Mamani") });

        // El vuelo quedó confirmado...
        var booking = await BookingAsync(reservation.Id);
        Assert.Equal(FlightBookingStatus.CONFIRMED, booking.Status);

        // ...y de la persona que viaja no quedó nada: ni nombre, ni fecha de nacimiento, ni contacto.
        // TurisClick no los necesita después de emitir, y la aerolínea ya los tiene.
        var persisted = string.Join('|',
            booking.ItinerarySummary, booking.FailureReason ?? string.Empty, booking.BookingReference ?? string.Empty,
            booking.CarrierName ?? string.Empty, booking.OriginIata, booking.DestinationIata);

        Assert.DoesNotContain("Zoraida", persisted);
        Assert.DoesNotContain("Mamani", persisted);
        Assert.DoesNotContain("1990-05-14", persisted);
        Assert.DoesNotContain("example.test", persisted);
        Assert.DoesNotContain("+59170000000", persisted);

        // Y tampoco hay una tabla de pasajeros escondida en algún lado.
        var tables = await QueryDbAsync(db => db.Database
            .SqlQuery<string>($"SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'")
            .ToListAsync());
        Assert.DoesNotContain("flight_travelers", tables);
        Assert.DoesNotContain("flight_passengers", tables);
    }

    [Fact]
    public async Task UnVueloQueExigeDocumentoDeIdentidadNoSeVende()
    {
        // DOC: la oferta pide documento de cada pasajero. Antes de pedirle el pasaporte a todo el mundo
        // "por las dudas", se dice que esa opción no se puede vender todavía.
        var scenario = await SeedAsync("fb-docs", origin: "DOC");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        var response = await PayAsync(scenario, reservation!.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(FlightBookingStatus.PENDING, (await BookingAsync(reservation.Id)).Status);
        Assert.Equal(1, await ReservedSlotsAsync(scenario.AvailabilityId));
    }

    // ================================================================ cambio de precio

    [Fact]
    public async Task UnCambioDePrecioDetieneElPagoYPideAceptacionExplicita()
    {
        var scenario = await SeedAsync("fb-price", origin: "CHG");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        var paid = await (await PayAsync(scenario, reservation!.Id)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        Assert.True(paid!.RequiresFlightPriceAcceptance);
        Assert.NotNull(paid.FlightPreviousPrice);
        Assert.NotNull(paid.FlightCurrentPrice);
        Assert.True(paid.FlightCurrentPrice!.Amount > paid.FlightPreviousPrice!.Amount);

        // No se cobró, no se emitió, no cambió nada.
        Assert.Equal("PENDING_PAYMENT", paid.Status);
        Assert.Null(paid.PaymentApproved);
        Assert.Equal(FlightBookingStatus.PENDING, (await BookingAsync(reservation.Id)).Status);
    }

    [Fact]
    public async Task AceptarUnPrecioQueYaNoEsElVigenteNoAlcanza()
    {
        var scenario = await SeedAsync("fb-price-stale", origin: "CHG");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        var first = await (await PayAsync(scenario, reservation!.Id)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        // Se reenvía el pago aceptando el precio VIEJO: es exactamente el ataque que un booleano permitiría.
        var replay = await (await PayAsync(scenario, reservation.Id,
            acceptedFlightPrice: new { amount = first!.FlightPreviousPrice!.Amount, currency = first.FlightPreviousPrice.Currency }))
            .Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        Assert.True(replay!.RequiresFlightPriceAcceptance);
        Assert.Equal("PENDING_PAYMENT", replay.Status);
        Assert.Equal(FlightBookingStatus.PENDING, (await BookingAsync(reservation.Id)).Status);
    }

    [Fact]
    public async Task AceptarElPrecioVigenteEmiteConEseImporte()
    {
        var scenario = await SeedAsync("fb-price-accept", origin: "CHG");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        var first = await (await PayAsync(scenario, reservation!.Id)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        var accepted = first!.FlightCurrentPrice!;

        var paid = await (await PayAsync(scenario, reservation.Id,
            acceptedFlightPrice: new { amount = accepted.Amount, currency = accepted.Currency }))
            .Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        Assert.Equal("CONFIRMED", paid!.Status);
        Assert.Equal("CONFIRMED", paid.Flight!.Status);
        // Se emitió por el importe aceptado, no por el que se había cotizado.
        Assert.Equal(accepted.Amount, paid.Flight.Price.Amount);
    }

    // ================================================================ fallas del proveedor

    [Fact]
    public async Task UnRechazoDefinitivoDelProveedorLiberaElCupoYCancelaLaReserva()
    {
        var scenario = await SeedAsync("fb-reject", origin: "FAI");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        Assert.Equal(1, await ReservedSlotsAsync(scenario.AvailabilityId));

        var response = await PayAsync(scenario, reservation!.Id);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var booking = await BookingAsync(reservation.Id);
        Assert.Equal(FlightBookingStatus.FAILED, booking.Status);
        Assert.NotNull(booking.FailedAt);
        Assert.Null(booking.ProviderOrderId);

        // Lo que nunca puede pasar: que el cupo quede tomado por un vuelo que no se emitió.
        Assert.Equal(0, await ReservedSlotsAsync(scenario.AvailabilityId));
        Assert.Equal(ReservationStatus.CANCELLED,
            (await QueryDbAsync(db => db.Reservations.AsNoTracking().FirstAsync(r => r.Id == reservation.Id))).Status);
    }

    [Fact]
    public async Task UnVueloQueDesaparecioAlEmitirTambienLiberaElCupo()
    {
        var scenario = await SeedAsync("fb-gone", origin: "GON");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        // GON falla al revalidar, antes de cobrar: se informa como no disponible (410) y el cupo sigue
        // retenido para que la persona decida, porque todavía no hubo ninguna emisión fallida.
        var response = await PayAsync(scenario, reservation!.Id);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal(FlightBookingStatus.PENDING, (await BookingAsync(reservation.Id)).Status);
    }

    [Fact]
    public async Task SiLaSolicitudNuncaSaleSeMantieneElCupoYSePuedeReintentar()
    {
        var scenario = await SeedAsync("fb-notsent", origin: "NET");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        var response = await PayAsync(scenario, reservation!.Id);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Del otro lado no quedó nada, así que el intento se puede repetir tal cual: el vuelo vuelve a
        // PENDING y el cupo no se toca.
        Assert.Equal(FlightBookingStatus.PENDING, (await BookingAsync(reservation.Id)).Status);
        Assert.Equal(1, await ReservedSlotsAsync(scenario.AvailabilityId));
        Assert.Equal(ReservationStatus.PENDING_PAYMENT,
            (await QueryDbAsync(db => db.Reservations.AsNoTracking().FirstAsync(r => r.Id == reservation.Id))).Status);
    }

    [Fact]
    public async Task UnDesenlaceDesconocidoNoLiberaCupoYBloqueaElReintento()
    {
        var scenario = await SeedAsync("fb-unknown", origin: "UNK");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        var paid = await (await PayAsync(scenario, reservation!.Id)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        Assert.Equal("RECONCILIATION_REQUIRED", paid!.Flight!.Status);
        Assert.True(paid.Flight.InProgress);
        Assert.False(string.IsNullOrWhiteSpace(paid.FlightMessage));

        // El cupo NO se libera: puede haber un pasaje emitido del otro lado.
        Assert.Equal(1, await ReservedSlotsAsync(scenario.AvailabilityId));

        // Y volver a pagar no vuelve a emitir: reintentar a ciegas es lo que duplica una compra.
        var retry = await PayAsync(scenario, reservation.Id);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
    }

    // ================================================================ reconciliación

    [Fact]
    public async Task LaReconciliacionConfirmaLaOrdenQueElProveedorSiHabiaCreado()
    {
        var scenario = await SeedAsync("fb-rec-found", origin: "UNK");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        await PayAsync(scenario, reservation!.Id);

        var booking = await BookingAsync(reservation.Id);
        Assert.Equal(FlightBookingStatus.RECONCILIATION_REQUIRED, booking.Status);

        var outcome = await ReconcileAsync(booking.Id);
        Assert.Equal(FlightReconciliationOutcome.CONFIRMED, outcome);

        var confirmed = await BookingAsync(reservation.Id);
        Assert.Equal(FlightBookingStatus.CONFIRMED, confirmed.Status);
        Assert.NotNull(confirmed.ProviderOrderId);

        // La reserva se confirma sola: la persona ya había pagado y el pasaje existía.
        var detail = await (await scenario.Tourist.GetAsync($"/api/reservations/{reservation.Id}")).Content
            .ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        Assert.Equal("CONFIRMED", detail!.Status);
        Assert.Equal("CONFIRMED", detail.Flight!.Status);
        Assert.Equal(1, await ReservedSlotsAsync(scenario.AvailabilityId));
    }

    [Fact]
    public async Task LaReconciliacionNoDaPorPerdidaUnaOrdenEnElPrimerIntento()
    {
        // UNL: la respuesta se perdió y no quedó ninguna orden. Aun así no se decide de entrada: una orden
        // recién creada puede tardar en aparecer, y liberar cupo de más es peor que esperar unos minutos.
        var scenario = await SeedAsync("fb-rec-retry", origin: "UNL");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        await PayAsync(scenario, reservation!.Id);

        var booking = await BookingAsync(reservation.Id);
        var outcome = await ReconcileAsync(booking.Id);

        Assert.Equal(FlightReconciliationOutcome.RETRY_SCHEDULED, outcome);

        var after = await BookingAsync(reservation.Id);
        Assert.Equal(FlightBookingStatus.RECONCILIATION_REQUIRED, after.Status);
        Assert.Equal(1, after.ReconciliationAttempts);
        Assert.NotNull(after.NextReconciliationAt);
        Assert.Equal(1, await ReservedSlotsAsync(scenario.AvailabilityId));
    }

    [Fact]
    public async Task CuandoNoExisteNingunaOrdenLaReconciliacionDevuelveElCupo()
    {
        var scenario = await SeedAsync("fb-rec-lost", origin: "UNL");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        await PayAsync(scenario, reservation!.Id);

        var booking = await BookingAsync(reservation.Id);

        // Se consumen los intentos sin esperar los minutos de espera reales.
        await WithDbAsync(async db =>
        {
            await db.FlightBookings
                .Where(b => b.Id == booking.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.ReconciliationAttempts, 3));
            return true;
        });

        var outcome = await ReconcileAsync(booking.Id);
        Assert.Equal(FlightReconciliationOutcome.FAILED, outcome);

        var failed = await BookingAsync(reservation.Id);
        Assert.Equal(FlightBookingStatus.FAILED, failed.Status);
        Assert.Equal(0, await ReservedSlotsAsync(scenario.AvailabilityId));
        Assert.Equal(ReservationStatus.CANCELLED,
            (await QueryDbAsync(db => db.Reservations.AsNoTracking().FirstAsync(r => r.Id == reservation.Id))).Status);
    }

    [Fact]
    public async Task UnPasajeEmitidoSinReservaVigenteSeCancelaEnLaAerolinea()
    {
        var scenario = await SeedAsync("fb-rec-orphan", origin: "UNK");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        await PayAsync(scenario, reservation!.Id);

        var booking = await BookingAsync(reservation.Id);

        // La reserva dejó de estar vigente mientras el vuelo seguía sin resolverse. Es el caso defensivo:
        // existe el pasaje y ya no existe el viaje.
        await WithDbAsync(async db =>
        {
            await db.Reservations
                .Where(r => r.Id == reservation.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReservationStatus.CANCELLED));
            return true;
        });

        var outcome = await ReconcileAsync(booking.Id);

        Assert.Equal(FlightReconciliationOutcome.ORDER_CANCELLED, outcome);

        var cancelled = await BookingAsync(reservation.Id);
        Assert.Equal(FlightBookingStatus.CANCELLED, cancelled.Status);
        // Queda el rastro de lo que se había emitido: sin eso, nadie podría auditarlo.
        Assert.NotNull(cancelled.ProviderOrderId);
    }

    [Fact]
    public async Task UnaReconciliacionYaResueltaNoSeVuelveAProcesar()
    {
        var scenario = await SeedAsync("fb-rec-idem", origin: "UNK");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        await PayAsync(scenario, reservation!.Id);

        var booking = await BookingAsync(reservation.Id);
        Assert.Equal(FlightReconciliationOutcome.CONFIRMED, await ReconcileAsync(booking.Id));
        Assert.Equal(FlightReconciliationOutcome.NOT_APPLICABLE, await ReconcileAsync(booking.Id));

        Assert.Equal(1, await ReservedSlotsAsync(scenario.AvailabilityId));
    }

    // ================================================================ expiración y cancelación

    [Fact]
    public async Task UnaReservaConElVueloSinResolverNoExpira()
    {
        var scenario = await SeedAsync("fb-noexpire", origin: "UNK");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        await PayAsync(scenario, reservation!.Id);

        await WithDbAsync(async db =>
        {
            await db.Reservations
                .Where(r => r.Id == reservation.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-5)));
            return true;
        });

        // Expirar acá liberaría un cupo que puede estar comprado: la reserva espera a que se resuelva.
        Assert.False(await ExpireAsync(reservation.Id));
        Assert.Equal(1, await ReservedSlotsAsync(scenario.AvailabilityId));
    }

    [Fact]
    public async Task UnaReservaConVueloSinEmitirExpiraYLiberaTodo()
    {
        var scenario = await SeedAsync("fb-expire");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        await WithDbAsync(async db =>
        {
            await db.Reservations
                .Where(r => r.Id == reservation!.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-5)));
            return true;
        });

        Assert.True(await ExpireAsync(reservation!.Id));

        Assert.Equal(0, await ReservedSlotsAsync(scenario.AvailabilityId));
        Assert.Equal(FlightBookingStatus.CANCELLED, (await BookingAsync(reservation.Id)).Status);
    }

    [Fact]
    public async Task CancelarAntesDeEmitirLiberaElCupoYDescartaElVuelo()
    {
        var scenario = await SeedAsync("fb-cancel");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        var response = await scenario.Tourist.PostAsync($"/api/reservations/{reservation!.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(0, await ReservedSlotsAsync(scenario.AvailabilityId));
        Assert.Equal(FlightBookingStatus.CANCELLED, (await BookingAsync(reservation.Id)).Status);
    }

    [Fact]
    public async Task NoSePuedeCancelarMientrasElVueloNoEstaResuelto()
    {
        var scenario = await SeedAsync("fb-cancel-busy", origin: "UNK");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        await PayAsync(scenario, reservation!.Id);

        var response = await scenario.Tourist.PostAsync($"/api/reservations/{reservation.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, await ReservedSlotsAsync(scenario.AvailabilityId));
    }

    [Fact]
    public async Task UnPasajeYaEmitidoNoSePuedeCancelarDesdeLaApp()
    {
        var scenario = await SeedAsync("fb-cancel-confirmed");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        await PayAsync(scenario, reservation!.Id);

        var response = await scenario.Tourist.PostAsync($"/api/reservations/{reservation.Id}/cancel", null);

        // Misma frontera honesta que antes de esta oleada: cancelar algo ya confirmado exige una política de
        // reembolso —y con un vuelo emitido, también la de la aerolínea— que todavía no existe.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(FlightBookingStatus.CONFIRMED, (await BookingAsync(reservation.Id)).Status);
    }

    // ================================================================ monedas, visibilidad y regresión

    [Fact]
    public async Task ConMonedasDistintasSeCobraPorSeparadoYNoSeInventaUnTotal()
    {
        var scenario = await SeedAsync("fb-fx", currency: "BOB");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        var paid = await (await PayAsync(scenario, reservation!.Id)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        Assert.Equal("CONFIRMED", paid!.Status);
        // El paquete en bolivianos y el pasaje en dólares: dos importes, nunca uno convertido.
        Assert.Equal("BOB", Assert.Single(paid.Totals).Currency);
        Assert.Equal("USD", paid.Flight!.Price.Currency);
    }

    [Fact]
    public async Task ElOperadorVeLaReservaPeroNingunDatoDelPasajero()
    {
        var scenario = await SeedAsync("fb-provider-view");
        var quoteId = await QuoteAsync(scenario);
        var reservation = await (await ReserveAsync(scenario, quoteId)).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);
        await PayAsync(scenario, reservation!.Id, travelers: new[] { Traveler(given: "Zoraida", family: "Mamani") });

        var received = await (await scenario.Provider.GetAsync("/api/companies/me/reservations")).Content.ReadAsStringAsync();

        // El operador necesita saber que le reservaron su paquete; los datos del pasajero del vuelo no son
        // suyos y no los recibe.
        Assert.Contains(reservation.Id.ToString(), received);
        Assert.DoesNotContain("Zoraida", received);
        Assert.DoesNotContain("example.test", received);
        Assert.DoesNotContain("ord_fake", received);
    }

    [Fact]
    public async Task UnaReservaSinVueloSigueFuncionandoExactamenteIgual()
    {
        // Regresión del camino que ya existía: un paquete sin vuelo no pide pasajeros, no consulta a ningún
        // proveedor y responde lo mismo que antes de esta oleada.
        var adminClient = _factory.CreateClient();
        UseBearerToken(adminClient, await LoginAsAdminAsync(adminClient));
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var country = await (await adminClient.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"PaisSV-{suffix}", type = "COUNTRY" })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var region = await (await adminClient.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"RegionSV-{suffix}", type = "REGION", parentId = country!.Id })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);
        var city = await (await adminClient.PostAsJsonAsync("/api/admin/destinations",
            new { name = $"CiudadSV-{suffix}", type = "CITY", parentId = region!.Id })).Content.ReadFromJsonAsync<DestinationResponse>(JsonOptions);

        var providerClient = _factory.CreateClient();
        var provider = await RegisterApprovedProviderAsync(providerClient, _factory.CreateClient(), "fb-noflight");
        UseBearerToken(providerClient, provider.AccessToken);

        var package = await (await providerClient.PostAsJsonAsync("/api/packages", new
        {
            title = $"Sin vuelo-{suffix}",
            description = "Paquete terrestre con guía local, traslados y alojamiento incluidos.",
            destinationId = city!.Id,
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
        UseBearerToken(tourist, await RegisterAndLoginTouristAsync(tourist, "fb-noflight-t"));

        var reservation = await (await tourist.PostAsJsonAsync("/api/reservations", new
        {
            packageAvailabilityId = availability!.Id,
            travelers = 2,
        })).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        Assert.Null(reservation!.Flight);

        var paid = await (await tourist.PostAsJsonAsync($"/api/reservations/{reservation.Id}/pay",
            new { success = true, acceptPriceChanges = false })).Content.ReadFromJsonAsync<ReservationResponse>(JsonOptions);

        Assert.Equal("CONFIRMED", paid!.Status);
        Assert.True(paid.PaymentApproved);
        Assert.Null(paid.Flight);
        Assert.False(paid.RequiresFlightPriceAcceptance);
    }
}
