using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TurisClick.Api.Modules.Flights.Services;

/// <summary>
/// Aeropuertos del mundo, para buscar y para mostrar.
///
/// Decisión deliberada: **no hay tabla de aeropuertos**. Un código IATA es un identificador estable y el
/// dataset es de sólo lectura, así que una tabla agregaría un CRUD, una migración y una sincronización sin
/// darle nada al producto. Los datos salen de OurAirports (dominio público), filtrados a aeropuertos con
/// servicio regular de pasajeros, y viajan embebidos en el ensamblado: ver `tools/airports`.
///
/// Encima del dataset hay una capa curada con nombres en español —"Santa Cruz de la Sierra", "Londres"—
/// que es la que se muestra. La capa curada es también la lista corta del Backoffice, que no cambia.
///
/// Los códigos que no están en ninguna de las dos **no se rechazan acá**: se aceptan si tienen forma
/// válida y se muestran por su código. Un paquete a un aeropuerto raro no puede depender de este archivo;
/// quien sí exige un aeropuerto conocido (la búsqueda suelta) lo valida con <see cref="Find"/>.
/// </summary>
public static partial class AirportCatalog
{
    /// <summary>
    /// Un aeropuerto. `Country` es el nombre en español; `CountryCode`, el ISO 3166-1 alfa-2. Las
    /// coordenadas son aproximadas (dos decimales): alcanzan para distancias, no para navegar.
    /// </summary>
    public record Airport(
        string Iata,
        string City,
        string Name,
        string Country,
        string CountryCode = "",
        double? Latitude = null,
        double? Longitude = null,
        AirportSize Size = AirportSize.Small);

    public enum AirportSize { Large, Medium, Small }

    /// <summary>A qué lado del viaje va el aeropuerto que se busca.</summary>
    public enum SearchRole { Origin, Destination }

    public const string BoliviaCountryCode = "BO";

    /// <summary>Lo que el producto usa a diario, con nombres en español. Es también la lista del Backoffice.</summary>
    private static readonly Dictionary<string, Airport> Curated = new(StringComparer.Ordinal)
    {
        ["VVI"] = new("VVI", "Santa Cruz de la Sierra", "Viru Viru", "Bolivia", "BO"),
        ["LPB"] = new("LPB", "La Paz", "El Alto", "Bolivia", "BO"),
        ["CBB"] = new("CBB", "Cochabamba", "Jorge Wilstermann", "Bolivia", "BO"),
        ["SRE"] = new("SRE", "Sucre", "Alcantarí", "Bolivia", "BO"),
        ["TJA"] = new("TJA", "Tarija", "Oriel Lea Plaza", "Bolivia", "BO"),
        ["POI"] = new("POI", "Potosí", "Capitán Nicolás Rojas", "Bolivia", "BO"),
        ["TDD"] = new("TDD", "Trinidad", "Jorge Henrich Arauz", "Bolivia", "BO"),
        ["RBQ"] = new("RBQ", "Rurrenabaque", "Rurrenabaque", "Bolivia", "BO"),
        ["UYU"] = new("UYU", "Uyuni", "Joya Andina", "Bolivia", "BO"),
        ["LIM"] = new("LIM", "Lima", "Jorge Chávez", "Perú", "PE"),
        ["EZE"] = new("EZE", "Buenos Aires", "Ezeiza", "Argentina", "AR"),
        ["GRU"] = new("GRU", "São Paulo", "Guarulhos", "Brasil", "BR"),
        ["SCL"] = new("SCL", "Santiago", "Arturo Merino Benítez", "Chile", "CL"),
        ["BOG"] = new("BOG", "Bogotá", "El Dorado", "Colombia", "CO"),
        ["MAD"] = new("MAD", "Madrid", "Barajas", "España", "ES"),
        ["MIA"] = new("MIA", "Miami", "Miami International", "Estados Unidos", "US"),
        ["JFK"] = new("JFK", "Nueva York", "John F. Kennedy", "Estados Unidos", "US"),
        ["LHR"] = new("LHR", "Londres", "Heathrow", "Reino Unido", "GB"),
        ["AMM"] = new("AMM", "Amán", "Queen Alia", "Jordania", "JO"),
        ["CUZ"] = new("CUZ", "Cusco", "Alejandro Velasco Astete", "Perú", "PE"),
    };

    /// <summary>
    /// Ciudades del dataset que en español se llaman distinto, por ciudad Y país: London (Reino Unido) es
    /// Londres, London (Canadá) no. Se usan para mostrar y también para buscar: "londres" y "london" tienen
    /// que encontrar los mismos aeropuertos.
    /// </summary>
    private static readonly Dictionary<(string City, string Country), string> SpanishCityNames = new()
    {
        [("London", "GB")] = "Londres", [("New York", "US")] = "Nueva York", [("Mexico City", "MX")] = "Ciudad de México",
        [("Rome", "IT")] = "Roma", [("Paris", "FR")] = "París", [("Munich", "DE")] = "Múnich", [("Geneva", "CH")] = "Ginebra",
        [("Brussels", "BE")] = "Bruselas", [("Lisbon", "PT")] = "Lisboa", [("Moscow", "RU")] = "Moscú", [("Tokyo", "JP")] = "Tokio",
        [("Beijing", "CN")] = "Pekín", [("Cairo", "EG")] = "El Cairo", [("Amman", "JO")] = "Amán", [("Athens", "GR")] = "Atenas",
        [("Florence", "IT")] = "Florencia", [("Milan", "IT")] = "Milán", [("Venice", "IT")] = "Venecia", [("Seville", "ES")] = "Sevilla",
        [("Zurich", "CH")] = "Zúrich", [("Frankfurt am Main", "DE")] = "Fráncfort", [("Cologne", "DE")] = "Colonia",
        [("Copenhagen", "DK")] = "Copenhague", [("Stockholm", "SE")] = "Estocolmo", [("Vienna", "AT")] = "Viena",
        [("Prague", "CZ")] = "Praga", [("Warsaw", "PL")] = "Varsovia", [("Istanbul", "TR")] = "Estambul",
        [("Havana", "CU")] = "La Habana", [("Philadelphia", "US")] = "Filadelfia", [("New Orleans", "US")] = "Nueva Orleans",
        [("Bogota", "CO")] = "Bogotá", [("Asuncion", "PY")] = "Asunción", [("Panama City", "PA")] = "Ciudad de Panamá",
        [("Guatemala City", "GT")] = "Ciudad de Guatemala", [("Sao Paulo", "BR")] = "São Paulo",
        [("Edinburgh", "GB")] = "Edimburgo", [("Dublin", "IE")] = "Dublín", [("Berlin", "DE")] = "Berlín",
        [("Hamburg", "DE")] = "Hamburgo",
    };

    // ---------------------------------------------------------------- carga del dataset

    private sealed record Entry(Airport Airport, string NormalizedCity, string NormalizedName, string NormalizedCountry, string[] Words);

    private sealed record Dataset(IReadOnlyDictionary<string, Entry> ByCode);

    private static readonly Lazy<Dataset> Data = new(LoadDataset, LazyThreadSafetyMode.ExecutionAndPublication);

    private static Dataset LoadDataset()
    {
        using var stream = typeof(AirportCatalog).Assembly.GetManifestResourceStream("TurisClick.Airports.json")
            ?? throw new InvalidOperationException("Falta el dataset de aeropuertos embebido (TurisClick.Airports.json).");
        using var document = JsonDocument.Parse(stream);

        var countries = document.RootElement.GetProperty("countries").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? p.Name, StringComparer.Ordinal);

        var byCode = new Dictionary<string, Entry>(StringComparer.Ordinal);

        foreach (var row in document.RootElement.GetProperty("airports").EnumerateArray())
        {
            var iata = row[0].GetString()!;
            var name = row[1].GetString() ?? iata;
            var rawCity = row[2].GetString() ?? string.Empty;
            var countryCode = row[3].GetString() ?? string.Empty;
            var size = row[6].GetString() switch { "L" => AirportSize.Large, "M" => AirportSize.Medium, _ => AirportSize.Small };

            var airport = new Airport(
                iata,
                SpanishCityNames.GetValueOrDefault((rawCity, countryCode)) ?? (string.IsNullOrWhiteSpace(rawCity) ? name : rawCity),
                name,
                countries.GetValueOrDefault(countryCode) ?? countryCode,
                countryCode,
                row[4].GetDouble(),
                row[5].GetDouble(),
                size);

            // La capa curada manda sobre cómo se llama la ciudad y el aeropuerto, pero el dataset aporta lo
            // que la curada no tiene: coordenadas y tamaño.
            if (Curated.TryGetValue(iata, out var curated))
                airport = airport with { City = curated.City, Name = curated.Name, Country = curated.Country };

            byCode[iata] = BuildEntry(airport, rawCity);
        }

        // Los curados que el dataset no trae (p. ej. POI, sin servicio regular hoy) se siguen pudiendo
        // describir y validar: el Backoffice ya los ofrece.
        foreach (var curated in Curated.Values.Where(c => !byCode.ContainsKey(c.Iata)))
            byCode[curated.Iata] = BuildEntry(curated, curated.City);

        return new Dataset(byCode);
    }

    private static Entry BuildEntry(Airport airport, string rawCity)
    {
        var city = Fold(airport.City);
        var name = Fold(airport.Name);
        var country = Fold(airport.Country);
        var alias = Fold(rawCity);

        var words = $"{city} {alias} {name} {country}"
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Distinct()
            .ToArray();

        return new Entry(airport, alias == city ? city : $"{city} {alias}", name, country, words);
    }

    // ---------------------------------------------------------------- API

    /// <summary>La lista corta curada, tal como la usa el Backoffice desde siempre.</summary>
    public static IReadOnlyCollection<Airport> All => Curated.Values;

    /// <summary>Los bolivianos de la lista curada: los que el operador elige el 99% de las veces.</summary>
    public static IReadOnlyCollection<Airport> Bolivian =>
        [.. Curated.Values.Where(a => a.CountryCode == BoliviaCountryCode)];

    /// <summary>
    /// Los aeropuertos de Bolivia con servicio regular de pasajeros según el dataset. Son los destinos que
    /// admite la búsqueda suelta de vuelos.
    /// </summary>
    public static IReadOnlyCollection<Airport> BolivianDestinations =>
        [.. Data.Value.ByCode.Values
            .Select(e => e.Airport)
            .Where(a => a.CountryCode == BoliviaCountryCode && a.Latitude is not null)
            .OrderBy(a => a.Size)
            .ThenBy(a => a.City, StringComparer.CurrentCulture)];

    public static bool IsValidCode(string? code) => code is not null && IataRegex().IsMatch(code);

    /// <summary>Mayúsculas y sin espacios. Un código es un identificador: "vvi " y "VVI" son el mismo.</summary>
    public static string Normalize(string code) => code.Trim().ToUpperInvariant();

    /// <summary>El aeropuerto, si lo conocemos (curado o del dataset). Null si no: nunca uno inventado.</summary>
    public static Airport? Find(string code) => Data.Value.ByCode.GetValueOrDefault(Normalize(code))?.Airport;

    /// <summary>Si el aeropuerto puede ser destino de una búsqueda suelta: hoy, sólo Bolivia con vuelos regulares.</summary>
    public static bool IsBolivianDestination(string code) =>
        Find(code) is { CountryCode: BoliviaCountryCode, Latitude: not null };

    /// <summary>"Santa Cruz de la Sierra (VVI)" si lo conocemos; si no, el código solo. Nunca un nombre inventado.</summary>
    public static string Describe(string code)
    {
        var normalized = Normalize(code);
        var airport = Find(normalized);
        return airport is null ? normalized : $"{airport.City} ({normalized})";
    }

    /// <summary>
    /// Dos aeropuertos de la misma ciudad (Viru Viru y El Trompillo) no son un viaje que tenga sentido
    /// buscar. Se decide por distancia y no por el nombre de la ciudad, porque el dataset la escribe distinto
    /// según el aeropuerto ("Santa Cruz", "Santa Cruz de la Sierra"). Si falta una coordenada, no se afirma nada.
    /// </summary>
    public static bool SameCity(string a, string b) => DistanceKm(a, b) is < SameCityRadiusKm;

    /// <summary>Viru Viru y El Trompillo están a ~20 km; dos ciudades distintas con aeropuerto, a bastante más.</summary>
    private const double SameCityRadiusKm = 50;

    /// <summary>Distancia ortodrómica en kilómetros, o null si falta alguna coordenada.</summary>
    public static double? DistanceKm(string a, string b)
    {
        if (Find(a) is not { Latitude: { } lat1, Longitude: { } lon1 }) return null;
        if (Find(b) is not { Latitude: { } lat2, Longitude: { } lon2 }) return null;

        const double earthRadiusKm = 6371;
        static double Rad(double degrees) => degrees * Math.PI / 180;

        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * earthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    /// <summary>
    /// Autocompletado: por código IATA, ciudad (en español o como la publica el dataset), nombre del
    /// aeropuerto y país, sin distinguir mayúsculas ni tildes.
    ///
    /// El orden imita a los buscadores de vuelos: el código exacto primero; después la ciudad que empieza
    /// con lo escrito; después el nombre del aeropuerto; el país al final, porque "bra" busca Brasilia antes
    /// que cualquier aeropuerto de Brasil. A igual coincidencia, el aeropuerto más grande va antes.
    ///
    /// Sin texto devuelve sugerencias: los destinos de Bolivia, o los orígenes más habituales.
    /// </summary>
    public static IReadOnlyList<Airport> Search(string? query, SearchRole role, int limit)
    {
        var candidates = Data.Value.ByCode.Values.Where(e =>
            role == SearchRole.Origin
                ? e.Airport.Latitude is not null || Curated.ContainsKey(e.Airport.Iata)
                : e.Airport.CountryCode == BoliviaCountryCode && e.Airport.Latitude is not null);

        var folded = Fold(query ?? string.Empty);

        if (folded.Length == 0)
        {
            return
            [
                .. candidates
                    .Where(e => role == SearchRole.Destination || Curated.ContainsKey(e.Airport.Iata))
                    .OrderBy(e => e.Airport.CountryCode == BoliviaCountryCode ? 0 : 1)
                    .ThenBy(e => e.Airport.Size)
                    .ThenBy(e => e.Airport.City, StringComparer.CurrentCulture)
                    .Take(limit)
                    .Select(e => e.Airport),
            ];
        }

        var tokens = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return
        [
            .. candidates
                .Select(e => (Entry: e, Score: Score(e, folded, tokens)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                // A igual coincidencia, el aeropuerto principal de la ciudad (la capa curada) va primero:
                // "londres" es Heathrow antes que Gatwick.
                .ThenBy(x => Curated.ContainsKey(x.Entry.Airport.Iata) ? 0 : 1)
                .ThenBy(x => x.Entry.Airport.Size)
                .ThenBy(x => x.Entry.Airport.City, StringComparer.CurrentCulture)
                .ThenBy(x => x.Entry.Airport.Iata, StringComparer.Ordinal)
                .Take(limit)
                .Select(x => x.Entry.Airport),
        ];
    }

    private static int Score(Entry entry, string query, string[] tokens)
    {
        var iata = entry.Airport.Iata.ToLowerInvariant();

        if (query == iata) return 1000;
        if (query.Length < 3 && iata.StartsWith(query, StringComparison.Ordinal) && query.Length >= 2) return 450;
        if (entry.NormalizedCity.StartsWith(query, StringComparison.Ordinal)) return 400;
        if (WordStartsWith(entry.NormalizedCity, query)) return 350;
        if (entry.NormalizedName.StartsWith(query, StringComparison.Ordinal)) return 300;
        if (WordStartsWith(entry.NormalizedName, query)) return 250;
        if (entry.NormalizedCountry.StartsWith(query, StringComparison.Ordinal)) return 150;

        // Varias palabras ("la paz bolivia", "santa cruz viru"): cada una tiene que empezar alguna palabra
        // del aeropuerto. Es lo que permite refinar sin escribir el nombre exacto.
        if (tokens.Length > 1 && tokens.All(t => entry.Words.Any(w => w.StartsWith(t, StringComparison.Ordinal))))
            return 100;

        return 0;
    }

    /// <summary>Alguna palabra que no es la primera empieza con lo escrito ("paz" encuentra "La Paz").</summary>
    private static bool WordStartsWith(string text, string query) =>
        text.Contains(' ' + query, StringComparison.Ordinal);

    /// <summary>Minúsculas, sin tildes y con un solo espacio entre palabras: "São Paulo" y "sao  paulo" son lo mismo.</summary>
    private static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = true;

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex IataRegex();
}
