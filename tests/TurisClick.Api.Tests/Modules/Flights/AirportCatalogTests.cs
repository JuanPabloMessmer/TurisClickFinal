using TurisClick.Api.Modules.Flights.Services;
using Xunit;
using static TurisClick.Api.Modules.Flights.Services.AirportCatalog;

namespace TurisClick.Api.Tests.Modules.Flights;

/// <summary>
/// El autocompletado de aeropuertos. Lo que se protege es lo que un buscador de vuelos tiene que hacer bien:
/// encontrar por código, por ciudad (en español o en inglés) y por nombre, sin que las tildes importen, con
/// el código exacto primero; y, como destino, ofrecer sólo Bolivia.
/// </summary>
public class AirportCatalogTests
{
    private static List<string> Codes(string? query, SearchRole role = SearchRole.Origin, int limit = 10) =>
        [.. Search(query, role, limit).Select(a => a.Iata)];

    [Fact]
    public void ElDatasetEmbebidoTraeAeropuertosDeTodoElMundo()
    {
        Assert.NotNull(Find("MAD"));
        Assert.NotNull(Find("NRT"));
        Assert.NotNull(Find("SYD"));
        Assert.NotNull(Find("JNB"));
        Assert.Null(Find("ZZZ"));
    }

    [Fact]
    public void ElCodigoExactoVaPrimero()
    {
        Assert.Equal("MAD", Codes("mad")[0]);
        Assert.Equal("LPB", Codes("LPB")[0]);
    }

    [Fact]
    public void LaCiudadSeEncuentraEnEspanolYEnIngles()
    {
        var spanish = Codes("londres");
        var english = Codes("london");

        Assert.Contains("LHR", spanish);
        Assert.Contains("LGW", spanish);
        // A igual coincidencia, el aeropuerto más grande va antes.
        Assert.Equal("LHR", spanish[0]);
        Assert.Contains("LHR", english);
        Assert.Equal("Londres", Find("LGW")!.City);
    }

    [Fact]
    public void LasTildesYLasMayusculasNoImportan()
    {
        Assert.Contains("GRU", Codes("sao paulo"));
        Assert.Contains("GRU", Codes("SÃO PAULO"));
        Assert.Contains("POI", Codes("potosi"));
    }

    [Fact]
    public void SeEncuentraPorNombreDelAeropuerto()
    {
        Assert.Contains("VVI", Codes("viru viru"));
        Assert.Contains("JFK", Codes("kennedy"));
    }

    [Fact]
    public void SeEncuentraPorPais_YVariasPalabrasRefinan()
    {
        Assert.All(Search("bolivia", SearchRole.Origin, 25), a => Assert.Equal("BO", a.CountryCode));
        Assert.Equal("VVI", Codes("santa cruz viru")[0]);
    }

    [Fact]
    public void ElDestinoSoloOfreceBolivia()
    {
        Assert.Empty(Codes("madrid", SearchRole.Destination));
        Assert.All(Search("a", SearchRole.Destination, 25), a => Assert.Equal("BO", a.CountryCode));

        var santaCruz = Codes("santa cruz", SearchRole.Destination);
        Assert.Equal("VVI", santaCruz[0]);
        Assert.Contains("SRZ", santaCruz);
    }

    [Fact]
    public void SinTextoElDestinoSugiereTodosLosAeropuertosBolivianosConVuelosRegulares()
    {
        var suggestions = Codes(null, SearchRole.Destination, 25);

        // No sólo los tres grandes: todos los que tienen servicio regular según el dataset.
        foreach (var code in new[] { "VVI", "LPB", "CBB", "SRE", "TJA", "UYU", "TDD", "RBQ", "CIJ", "RIB", "GYA", "ORU", "PSZ" })
            Assert.Contains(code, suggestions);

        Assert.Equal(suggestions.Count, BolivianDestinations.Count);
        Assert.True(IsBolivianDestination("CIJ"));
        Assert.False(IsBolivianDestination("LIM"));
    }

    [Fact]
    public void SinTextoElOrigenSugiereLosHabituales_BoliviaPrimero()
    {
        var suggestions = Search(null, SearchRole.Origin, 25);

        Assert.NotEmpty(suggestions);
        Assert.Equal("BO", suggestions[0].CountryCode);
        Assert.Contains(suggestions, a => a.Iata == "MAD");
    }

    [Fact]
    public void LaEtiquetaDeLosCuradosNoCambia()
    {
        // La usan el Backoffice, la cotización del paquete y los tests de toda la API.
        Assert.Equal("Santa Cruz de la Sierra (VVI)", Describe("VVI"));
        Assert.Equal("La Paz (LPB)", Describe("lpb"));
        Assert.Equal("ZZZ", Describe("ZZZ"));
        Assert.Equal(20, All.Count);
        Assert.Equal(9, Bolivian.Count);
    }

    [Fact]
    public void DosAeropuertosDeLaMismaCiudadSeReconocen()
    {
        Assert.True(SameCity("VVI", "SRZ"));
        Assert.False(SameCity("VVI", "LPB"));
        Assert.False(SameCity("VVI", "ZZZ"));
    }

    [Fact]
    public void LaDistanciaEsRazonable()
    {
        var domestic = DistanceKm("VVI", "LPB")!.Value;
        var transatlantic = DistanceKm("MAD", "VVI")!.Value;

        Assert.InRange(domestic, 500, 600);
        Assert.InRange(transatlantic, 8500, 9300);
        Assert.Null(DistanceKm("VVI", "ZZZ"));
    }
}
