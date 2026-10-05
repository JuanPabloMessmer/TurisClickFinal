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
}
