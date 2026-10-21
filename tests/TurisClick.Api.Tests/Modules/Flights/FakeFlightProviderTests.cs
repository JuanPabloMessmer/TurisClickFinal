using TurisClick.Api.Modules.Flights.Services;
using TurisClick.Api.Modules.Flights.Services.Providers;
using Xunit;

namespace TurisClick.Api.Tests.Modules.Flights;

/// <summary>
/// El proveedor falso es el que corre en CI, en el entorno desplegado y en cualquier demo sin red, así
/// que tiene que reproducir los casos difíciles del inventario aéreo y no sólo el camino feliz: oferta
/// vencida, precio que cambia al revalidar y oferta que desaparece.
/// </summary>
public class FakeFlightProviderTests
{
    private static readonly DateOnly Departure = new(2026, 11, 19);

    private static FlightSearchRequest Request(string origin, string destination = "LPB", int adults = 1) =>
        new([new FlightSliceRequest(origin, destination, Departure)], adults);

    private readonly FakeFlightProvider _sut = new();

    [Fact]
    public async Task ASearchReturnsOffersWithRealShape()
    {
        var result = await _sut.SearchAsync(Request("VVI"), CancellationToken.None);

        Assert.NotEmpty(result.Offers);
        var offer = result.Offers[0];
        Assert.Equal("USD", offer.Price.Currency);
        Assert.False(offer.LiveMode);
        Assert.Equal("VVI", offer.Slices[0].OriginIata);
        Assert.Equal("LPB", offer.Slices[0].DestinationIata);
        Assert.Single(offer.Passengers);
    }

    [Fact]
    public async Task ThePriceScalesWithThePassengerCount()
    {
        var one = (await _sut.SearchAsync(Request("VVI", adults: 1), CancellationToken.None)).Offers[0];
        var three = (await _sut.SearchAsync(Request("VVI", adults: 3), CancellationToken.None)).Offers[0];

        Assert.Equal(one.Price.Amount * 3, three.Price.Amount);
        Assert.Equal(3, three.Passengers.Count);
    }

    [Fact]
    public async Task ARouteWithoutInventoryReturnsNothing_WhichIsNotAnError()
    {
        var result = await _sut.SearchAsync(Request(FakeFlightProvider.NoOffersOrigin), CancellationToken.None);

        Assert.Empty(result.Offers);
    }

    [Fact]
    public async Task AnExpiredOfferIsRecognisableBeforeTryingToBookIt()
    {
        var offer = (await _sut.SearchAsync(Request(FakeFlightProvider.ExpiredOrigin), CancellationToken.None)).Offers[0];

        Assert.True(offer.IsExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task RevalidatingCanReturnADifferentPrice()
    {
        var offer = (await _sut.SearchAsync(Request(FakeFlightProvider.PriceChangeOrigin), CancellationToken.None)).Offers[0];

        var refreshed = await _sut.RefreshOfferAsync(offer.Id, CancellationToken.None);

        Assert.True(refreshed.Price.DiffersFrom(offer.Price));
        Assert.Equal(offer.Price.Amount + 75m, refreshed.Price.Amount);
    }

    [Fact]
    public async Task AnOfferThatDisappearedFailsOnRevalidation_NotOnBooking()
    {
        var offer = (await _sut.SearchAsync(Request(FakeFlightProvider.GoneOrigin), CancellationToken.None)).Offers[0];

        await Assert.ThrowsAsync<FlightOfferExpiredException>(
            () => _sut.RefreshOfferAsync(offer.Id, CancellationToken.None));
    }

    [Fact]
    public async Task AnOrderEchoesTheConfirmedPriceAndNeverClaimsToBeLive()
    {
        var offer = (await _sut.SearchAsync(Request("VVI"), CancellationToken.None)).Offers[0];
        var confirmed = new FlightPrice(offer.Price.Amount, offer.Price.Currency);

        var order = await _sut.CreateOrderAsync(
            new FlightOrderRequest(offer.Id, confirmed, [
                new FlightPassengerDetails(offer.Passengers[0].ProviderPassengerId, "Prueba", "Demostracion",
                    new DateOnly(1990, 1, 1), "m", "mr", "qa@example.com", "+59170000000"),
            ]),
            CancellationToken.None);

        Assert.False(order.LiveMode);
        Assert.Equal(confirmed.Amount, order.Price.Amount);
        Assert.NotNull(order.BookingReference);
    }

    [Fact]
    public async Task TheOfferIdCarriesItsOwnSearch_SoTwoInstancesAgree()
    {
        var offer = (await new FakeFlightProvider().SearchAsync(Request("VVI"), CancellationToken.None)).Offers[0];

        // Otra instancia, sin estado compartido, resuelve la misma oferta: es lo que permite que un test
        // busque en un servicio y revalide en otro.
        var refreshed = await new FakeFlightProvider().RefreshOfferAsync(offer.Id, CancellationToken.None);

        Assert.Equal(offer.Id, refreshed.Id);
        Assert.Equal(offer.Price.Amount, refreshed.Price.Amount);
    }

    // ---------------------------------------------------------------- búsqueda suelta (Oleada 16)

    private static FlightSearchRequest Trip(
        string origin, string destination, DateOnly? back = null,
        FlightCabinClass cabin = FlightCabinClass.ECONOMY, int adults = 1, int[]? children = null, int infants = 0)
    {
        var slices = new List<FlightSliceRequest> { new(origin, destination, Departure) };
        if (back is { } date) slices.Add(new FlightSliceRequest(destination, origin, date));
        return new FlightSearchRequest(slices, adults, cabin, ChildAges: children, Infants: infants);
    }

    [Fact]
    public async Task UnaBusquedaTraeOfertasDistintas_ParaQueOrdenarYFiltrarTenganSentido()
    {
        var offers = (await _sut.SearchAsync(Trip("VVI", "LPB"), CancellationToken.None)).Offers;

        Assert.Equal(FakeFlightProvider.OffersPerSearch, offers.Count);
        Assert.Contains(offers, o => o.Slices[0].Segments.Count == 1);
        Assert.Contains(offers, o => o.Slices[0].Segments.Count == 2);
        Assert.Contains(offers, o => o.Slices[0].Segments[0].CheckedBags == 0);
        Assert.Contains(offers, o => o.Slices[0].Segments[0].CheckedBags == 1);
        Assert.Equal(offers.Count, offers.Select(o => o.Slices[0].Segments[0].DepartingAt.Hour).Distinct().Count());
    }

    [Fact]
    public async Task LasRutasCortasSeCotizanIgualQueAntes()
    {
        // El paquete con vuelo depende de esto: las dos primeras ofertas en económica, sólo ida y sólo adultos.
        var offers = (await _sut.SearchAsync(Request("VVI", adults: 2), CancellationToken.None)).Offers;

        Assert.Equal(240m, offers[0].Price.Amount);
        Assert.Equal(310m, offers[1].Price.Amount);
        Assert.Equal(new TimeOnly(8, 15), TimeOnly.FromDateTime(offers[0].Slices[0].Segments[0].DepartingAt));
        Assert.Equal(new TimeOnly(12, 15), TimeOnly.FromDateTime(offers[1].Slices[0].Segments[0].DepartingAt));
    }

    [Fact]
    public async Task UnVueloLargoDuraMasYCasiSiempreHaceEscala()
    {
        var domestic = (await _sut.SearchAsync(Trip("VVI", "LPB"), CancellationToken.None)).Offers[0];
        var longHaul = (await _sut.SearchAsync(Trip("MAD", "VVI"), CancellationToken.None)).Offers;

        Assert.True(longHaul[0].Slices[0].Duration > domestic.Slices[0].Duration);
        Assert.Single(longHaul[0].Slices[0].Segments);
        Assert.All(longHaul.Skip(1), o => Assert.Equal(2, o.Slices[0].Segments.Count));

        // La escala nunca es uno de los extremos del viaje.
        var stop = longHaul[1].Slices[0].Segments[0].DestinationIata;
        Assert.NotEqual("MAD", stop);
        Assert.NotEqual("VVI", stop);
        Assert.Equal(stop, longHaul[1].Slices[0].Segments[1].OriginIata);
    }

    [Fact]
    public async Task LaCabinaYLosPasajerosCambianElPrecio()
    {
        var economy = (await _sut.SearchAsync(Trip("VVI", "LPB"), CancellationToken.None)).Offers[0];
        var business = (await _sut.SearchAsync(Trip("VVI", "LPB", cabin: FlightCabinClass.BUSINESS), CancellationToken.None)).Offers[0];
        var family = (await _sut.SearchAsync(Trip("VVI", "LPB", adults: 2, children: [7], infants: 1), CancellationToken.None)).Offers[0];

        Assert.Equal(economy.Price.Amount * 3, business.Price.Amount);
        Assert.Equal(2, business.Slices[0].Segments[0].CheckedBags);

        // Dos adultos, un niño al 75% y un infante en brazos al 10%.
        Assert.Equal(economy.Price.Amount * 2.85m, family.Price.Amount);
        Assert.Equal(["adult", "adult", "child", "infant_without_seat"], family.Passengers.Select(p => p.Type));
    }

    [Fact]
    public async Task RevalidarUnaIdaYVueltaEnBusinessDevuelveElMismoPrecio()
    {
        // Si el id de la oferta perdiera la vuelta o la cabina, revalidar "cambiaría" el precio y el flujo de
        // reserva le pediría a la persona aceptar un aumento que nadie hizo.
        var offer = (await _sut.SearchAsync(
            Trip("VVI", "LPB", back: Departure.AddDays(6), cabin: FlightCabinClass.BUSINESS, adults: 2, children: [5]),
            CancellationToken.None)).Offers[1];

        var refreshed = await new FakeFlightProvider().RefreshOfferAsync(offer.Id, CancellationToken.None);

        Assert.Equal(offer.Price.Amount, refreshed.Price.Amount);
        Assert.Equal(2, refreshed.Slices.Count);
        Assert.Equal(offer.Passengers.Count, refreshed.Passengers.Count);
    }

    [Fact]
    public async Task UnIdDeOfertaDelFormatoAnteriorSeSigueLeyendo()
    {
        // Cotizaciones guardadas antes de este cambio siguen en flight_quotes y se tienen que poder revalidar.
        var refreshed = await _sut.RefreshOfferAsync("off_fake_VVI_LPB_20261119_2_1", CancellationToken.None);

        Assert.Equal(310m, refreshed.Price.Amount);
        Assert.Single(refreshed.Slices);
    }
}
