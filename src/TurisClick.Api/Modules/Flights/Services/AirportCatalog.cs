using System.Text.RegularExpressions;

namespace TurisClick.Api.Modules.Flights.Services;

/// <summary>
/// Aeropuertos, al tamaño que este producto necesita.
///
/// Decisión deliberada: **no hay tabla de aeropuertos**. Un código IATA es un identificador estable de
/// tres letras; lo que agrega una tabla es un CRUD, una migración y un dato más para mantener
/// desactualizado. Lo que sí hace falta es (a) validar la forma, (b) normalizar a mayúsculas y (c) poder
/// mostrar un nombre legible para los aeropuertos que el producto usa de verdad.
///
/// Los códigos que no están en la lista curada **no se rechazan**: se aceptan si tienen forma válida y
/// se muestran por su código. Un paquete a Amán no puede depender de que alguien haya cargado AMM acá.
/// </summary>
public static partial class AirportCatalog
{
    public record Airport(string Iata, string City, string Name, string Country);

    /// <summary>Lo que el producto usa a diario: Bolivia completa y los destinos de los paquetes del catálogo.</summary>
    private static readonly Dictionary<string, Airport> Known = new(StringComparer.Ordinal)
    {
        ["VVI"] = new("VVI", "Santa Cruz de la Sierra", "Viru Viru", "Bolivia"),
        ["LPB"] = new("LPB", "La Paz", "El Alto", "Bolivia"),
        ["CBB"] = new("CBB", "Cochabamba", "Jorge Wilstermann", "Bolivia"),
        ["SRE"] = new("SRE", "Sucre", "Alcantarí", "Bolivia"),
        ["TJA"] = new("TJA", "Tarija", "Oriel Lea Plaza", "Bolivia"),
        ["POI"] = new("POI", "Potosí", "Capitán Nicolás Rojas", "Bolivia"),
        ["TDD"] = new("TDD", "Trinidad", "Jorge Henrich Arauz", "Bolivia"),
        ["RBQ"] = new("RBQ", "Rurrenabaque", "Rurrenabaque", "Bolivia"),
        ["UYU"] = new("UYU", "Uyuni", "Joya Andina", "Bolivia"),
        ["LIM"] = new("LIM", "Lima", "Jorge Chávez", "Perú"),
        ["EZE"] = new("EZE", "Buenos Aires", "Ezeiza", "Argentina"),
        ["GRU"] = new("GRU", "São Paulo", "Guarulhos", "Brasil"),
        ["SCL"] = new("SCL", "Santiago", "Arturo Merino Benítez", "Chile"),
        ["BOG"] = new("BOG", "Bogotá", "El Dorado", "Colombia"),
        ["MAD"] = new("MAD", "Madrid", "Barajas", "España"),
        ["MIA"] = new("MIA", "Miami", "Miami International", "Estados Unidos"),
        ["JFK"] = new("JFK", "Nueva York", "John F. Kennedy", "Estados Unidos"),
        ["LHR"] = new("LHR", "Londres", "Heathrow", "Reino Unido"),
        ["AMM"] = new("AMM", "Amán", "Queen Alia", "Jordania"),
        ["CUZ"] = new("CUZ", "Cusco", "Alejandro Velasco Astete", "Perú"),
    };

    public static IReadOnlyCollection<Airport> All => Known.Values;

    /// <summary>Los que el operador va a elegir el 99% de las veces: se ofrecen primero en el Backoffice.</summary>
    public static IReadOnlyCollection<Airport> Bolivian =>
        [.. Known.Values.Where(a => a.Country == "Bolivia")];

    public static bool IsValidCode(string? code) => code is not null && IataRegex().IsMatch(code);

    /// <summary>Mayúsculas y sin espacios. Un código es un identificador: "vvi " y "VVI" son el mismo.</summary>
    public static string Normalize(string code) => code.Trim().ToUpperInvariant();

    public static Airport? Find(string code) => Known.GetValueOrDefault(Normalize(code));

    /// <summary>"Santa Cruz de la Sierra (VVI)" si lo conocemos; si no, el código solo. Nunca un nombre inventado.</summary>
    public static string Describe(string code)
    {
        var normalized = Normalize(code);
        var airport = Known.GetValueOrDefault(normalized);
        return airport is null ? normalized : $"{airport.City} ({normalized})";
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex IataRegex();
}
