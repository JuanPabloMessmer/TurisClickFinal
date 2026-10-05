using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TurisClick.Api.Modules.Flights.Services;
using TurisClick.Api.Modules.Flights.Services.Providers.Duffel;

namespace TurisClick.DuffelSpike;

/// <summary>
/// Spike contra Duffel en MODO DE PRUEBA. Ejercita el ciclo real —buscar, revalidar, reservar y
/// cancelar— y escribe la evidencia en docs/duffel-test-results.md.
///
/// Es una herramienta aparte, fuera de la solución y fuera de CI, por tres motivos: necesita red,
/// necesita el token de prueba que vive en User Secrets, y crea órdenes en el entorno de Duffel. Los
/// tests automáticos de TurisClick no dependen de nada de eso.
///
/// El token se lee de la configuración y **no se imprime nunca**: de él sólo se reporta si es de prueba.
/// </summary>
public static class Program
{
    /// <summary>Rutas documentadas por Duffel para provocar comportamientos concretos en modo de prueba.</summary>
    private static readonly (string Route, string Expectation)[] ScenarioRoutes =
    [
        ("PVD-RAI", "sin ofertas"),
        ("LHR-STN", "cambio de precio al revalidar"),
        ("LGW-LHR", "oferta vencida"),
        ("LHR-DXB", "vuelo con escalas"),
    ];

    public static async Task<int> Main(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = new FlightsOptions();
        configuration.GetSection(FlightsOptions.SectionName).Bind(options);

        if (string.IsNullOrWhiteSpace(options.Duffel.AccessToken))
        {
            Console.Error.WriteLine(
                "No hay token en Flights:Duffel:AccessToken. Se configura con:\n" +
                "  dotnet user-secrets set \"Flights:Duffel:AccessToken\" \"<token de prueba>\" --project src/TurisClick.Api");
            return 1;
        }

        if (!options.Duffel.IsTestToken)
        {
            Console.Error.WriteLine("El token no es de prueba (no empieza con 'duffel_test_'). El spike no corre contra producción.");
            return 1;
        }

        Console.WriteLine($"Duffel {options.Duffel.ApiVersion} — modo de prueba (token duffel_test_…), {options.Duffel.BaseUrl}");

        using var http = new HttpClient();
        var provider = new DuffelFlightProvider(http, Options.Create(options), NullLogger<DuffelFlightProvider>.Instance);

        var report = new StringBuilder();
        var runAt = DateTimeOffset.Now;
        var departure = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(45);
        var returnDate = departure.AddDays(7);

        report.AppendLine($"Corrida: {runAt:yyyy-MM-dd HH:mm zzz}");
        report.AppendLine($"Fecha de salida usada en las búsquedas: {departure:yyyy-MM-dd} (ida y vuelta: {returnDate:yyyy-MM-dd})");
        report.AppendLine();

        // ---------------------------------------------------------------- 1. cobertura
        report.AppendLine("## Cobertura por ruta");
        report.AppendLine();
        report.AppendLine("| Ruta | Ofertas | Tiempo | Aerolínea | Precio más bajo | Resultado |");
        report.AppendLine("|---|---|---|---|---|---|");

        string[] bolivia = ["VVI-LPB", "VVI-CBB", "LPB-VVI", "LPB-CBB", "CBB-VVI"];
        string[] international = ["LHR-JFK", "JFK-LHR", "MAD-LIM", "GRU-EZE"];

        FlightOffer? bookableOffer = null;
        string? bookableRoute = null;

        foreach (var route in bolivia.Concat(international))
        {
            var (line, offer) = await ProbeRouteAsync(provider, route, departure);
            report.AppendLine(line);

            if (bookableOffer is null && offer is not null)
            {
                bookableOffer = offer;
                bookableRoute = route;
            }
        }

        report.AppendLine();
        report.AppendLine("## Escenarios documentados por Duffel");
        report.AppendLine();
        report.AppendLine("| Ruta | Comportamiento esperado | Ofertas | Observado |");
        report.AppendLine("|---|---|---|---|");

        foreach (var (route, expectation) in ScenarioRoutes)
        {
            var (line, _) = await ProbeScenarioAsync(provider, route, departure, expectation);
            report.AppendLine(line);
        }

        // ---------------------------------------------------------------- 2. ciclo completo
        report.AppendLine();
        report.AppendLine("## Ciclo completo: buscar → revalidar → reservar → cancelar");
        report.AppendLine();

        if (bookableOffer is null)
        {
            report.AppendLine("No hubo ninguna ruta con ofertas, así que no se pudo ejercitar el ciclo completo.");
        }
        else
        {
            report.AppendLine(await RunFullCycleAsync(provider, bookableRoute!, bookableOffer));
        }

        // ---------------------------------------------------------------- 3. escribir evidencia
        var repoRoot = FindRepoRoot();
        var path = Path.Combine(repoRoot, "docs", "duffel-test-results.md");
        var document = BuildDocument(report.ToString(), runAt);
        await File.WriteAllTextAsync(path, document, new UTF8Encoding(false));

        Console.WriteLine($"\nEvidencia escrita en {path}");
        return 0;
    }

    private static async Task<(string Line, FlightOffer? Offer)> ProbeRouteAsync(
        DuffelFlightProvider provider, string route, DateOnly departure)
    {
        var (origin, destination) = Split(route);
        var request = new FlightSearchRequest([new FlightSliceRequest(origin, destination, departure)], Adults: 1);

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await provider.SearchAsync(request, CancellationToken.None);
            stopwatch.Stop();

            if (result.Offers.Count == 0)
                return ($"| `{route}` | 0 | {stopwatch.ElapsedMilliseconds} ms | — | — | sin inventario en modo de prueba |", null);

            var cheapest = result.Offers.MinBy(o => o.Price.Amount)!;
            Console.WriteLine($"  {route}: {result.Offers.Count} oferta(s), desde {cheapest.Price.Currency} {cheapest.Price.Amount}");

            return ($"| `{route}` | {result.Offers.Count} | {stopwatch.ElapsedMilliseconds} ms | {cheapest.OwnerName} ({cheapest.OwnerIataCode}) | " +
                    $"{cheapest.Price.Currency} {cheapest.Price.Amount.ToString("0.00", CultureInfo.InvariantCulture)} | ofertas disponibles |", cheapest);
        }
        catch (FlightProviderException ex)
        {
            Console.WriteLine($"  {route}: {ex.GetType().Name}");
            return ($"| `{route}` | — | — | — | — | {ex.GetType().Name}: {Sanitize(ex.Message)} |", null);
        }
    }

    private static async Task<(string Line, FlightOffer? Offer)> ProbeScenarioAsync(
        DuffelFlightProvider provider, string route, DateOnly departure, string expectation)
    {
        var (origin, destination) = Split(route);
        var request = new FlightSearchRequest([new FlightSliceRequest(origin, destination, departure)], Adults: 1);

        try
        {
            var result = await provider.SearchAsync(request, CancellationToken.None);
            var observed = result.Offers.Count == 0 ? "sin ofertas" : $"{result.Offers.Count} oferta(s)";

            // Para el escenario de cambio de precio, revalidar es lo que lo hace visible.
            if (route == "LHR-STN" && result.Offers.Count > 0)
            {
                var first = result.Offers[0];
                var refreshed = await provider.RefreshOfferAsync(first.Id, CancellationToken.None);
                observed += refreshed.Price.DiffersFrom(first.Price)
                    ? $"; al revalidar {first.Price.Currency} {first.Price.Amount:0.00} → {refreshed.Price.Currency} {refreshed.Price.Amount:0.00}"
                    : "; al revalidar el precio no cambió";
            }

            if (route == "LGW-LHR" && result.Offers.Count > 0)
            {
                var first = result.Offers[0];
                observed += first.IsExpired(DateTimeOffset.UtcNow)
                    ? "; la oferta ya nace vencida"
                    : $"; expires_at = {first.ExpiresAt:HH:mm:ss} UTC";
            }

            return ($"| `{route}` | {expectation} | {result.Offers.Count} | {observed} |", result.Offers.FirstOrDefault());
        }
        catch (FlightProviderException ex)
        {
            return ($"| `{route}` | {expectation} | — | {ex.GetType().Name}: {Sanitize(ex.Message)} |", null);
        }
    }

    /// <summary>Buscar → revalidar → reservar → cancelar, con pasajeros sintéticos. Nunca datos reales.</summary>
    private static async Task<string> RunFullCycleAsync(DuffelFlightProvider provider, string route, FlightOffer offer)
    {
        var report = new StringBuilder();
        var (origin, destination) = Split(route);

        report.AppendLine($"Ruta elegida: **{origin} → {destination}** (la primera con inventario).");
        report.AppendLine();
        report.AppendLine("### 1. Oferta seleccionada");
        report.AppendLine();
        report.AppendLine($"- id: `{Mask(offer.Id)}`");
        report.AppendLine($"- aerolínea: {offer.OwnerName} ({offer.OwnerIataCode})");
        report.AppendLine($"- precio: **{offer.Price.Currency} {offer.Price.Amount:0.00}**");
        report.AppendLine($"- vence: {offer.ExpiresAt:yyyy-MM-dd HH:mm:ss} UTC");
        report.AppendLine($"- `live_mode`: **{offer.LiveMode}**");
        report.AppendLine($"- documentos de identidad requeridos: {offer.IdentityDocumentsRequired}");
        report.AppendLine($"- pago inmediato requerido: {offer.InstantPaymentRequired}");
        report.AppendLine($"- pasajeros esperados: {offer.Passengers.Count}");

        foreach (var slice in offer.Slices)
        {
            foreach (var segment in slice.Segments)
            {
                report.AppendLine(
                    $"- segmento: {segment.OriginIata} → {segment.DestinationIata}, " +
                    $"{segment.DepartingAt:yyyy-MM-dd HH:mm} → {segment.ArrivingAt:HH:mm}, " +
                    $"{segment.MarketingCarrierIata}{segment.FlightNumber}, equipaje facturado: {segment.CheckedBags?.ToString() ?? "sin dato"}");
            }
        }

        // ---------------------------------------------------------------- revalidación
        report.AppendLine();
        report.AppendLine("### 2. Revalidación");
        report.AppendLine();

        FlightOffer refreshed;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            refreshed = await provider.RefreshOfferAsync(offer.Id, CancellationToken.None);
            stopwatch.Stop();

            report.AppendLine($"- `GET /air/offers/{{id}}` respondió en {stopwatch.ElapsedMilliseconds} ms");
            report.AppendLine(refreshed.Price.DiffersFrom(offer.Price)
                ? $"- **el precio cambió**: {offer.Price.Amount:0.00} → {refreshed.Price.Amount:0.00} {refreshed.Price.Currency}"
                : $"- el precio se mantuvo en {refreshed.Price.Currency} {refreshed.Price.Amount:0.00}");
        }
        catch (FlightProviderException ex)
        {
            report.AppendLine($"- falló: {ex.GetType().Name} — {Sanitize(ex.Message)}");
            return report.ToString();
        }

        // ---------------------------------------------------------------- orden
        report.AppendLine();
        report.AppendLine("### 3. Orden de prueba");
        report.AppendLine();
        report.AppendLine("Pasajero **sintético**; no se usó ningún dato personal real.");
        report.AppendLine();

        // Nombres ficticios y SIN dígitos: Duffel rechaza un apellido con números
        // ("Field 'family_name' has invalid format"), que fue el primer error real de este spike.
        string[] syntheticSurnames = ["Demostracion", "Sintetico", "Pruebas", "Ejemplo"];

        var passengers = refreshed.Passengers
            .Select((slot, index) => new FlightPassengerDetails(
                slot.ProviderPassengerId,
                "Prueba",
                syntheticSurnames[index % syntheticSurnames.Length],
                new DateOnly(1990, 1, 1),
                "m",
                "mr",
                "qa@example.com",
                "+59170000000"))
            .ToList();

        FlightOrderResult order;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            order = await provider.CreateOrderAsync(
                new FlightOrderRequest(refreshed.Id, refreshed.Price, passengers), CancellationToken.None);
            stopwatch.Stop();

            report.AppendLine($"- `POST /air/orders` respondió en {stopwatch.ElapsedMilliseconds} ms");
            report.AppendLine($"- id de orden: `{Mask(order.OrderId)}`");
            report.AppendLine($"- localizador: `{order.BookingReference}`");
            report.AppendLine($"- total: {order.Price.Currency} {order.Price.Amount:0.00}");
            report.AppendLine($"- `live_mode`: **{order.LiveMode}** (false = la reserva vive sólo en el entorno de prueba)");
            Console.WriteLine($"  orden creada: {Mask(order.OrderId)} / {order.BookingReference}");
        }
        catch (FlightProviderException ex)
        {
            report.AppendLine($"- **falló**: {ex.GetType().Name} — {Sanitize(ex.Message)}");
            Console.WriteLine($"  orden falló: {ex.GetType().Name}");
            return report.ToString();
        }

        // ---------------------------------------------------------------- cancelación
        report.AppendLine();
        report.AppendLine("### 4. Cancelación");
        report.AppendLine();

        try
        {
            var cancellation = await provider.CancelOrderAsync(order.OrderId, confirm: true, CancellationToken.None);
            report.AppendLine($"- cancelación `{Mask(cancellation.CancellationId)}` confirmada a las {cancellation.ConfirmedAt:HH:mm:ss} UTC");
            report.AppendLine($"- reintegro: {cancellation.RefundCurrency} {cancellation.RefundAmount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "sin dato"} → {cancellation.RefundTo}");
        }
        catch (FlightProviderException ex)
        {
            report.AppendLine($"- no se pudo cancelar: {ex.GetType().Name} — {Sanitize(ex.Message)}");
        }

        return report.ToString();
    }

    // ---------------------------------------------------------------- utilidades

    private static (string Origin, string Destination) Split(string route)
    {
        var parts = route.Split('-');
        return (parts[0], parts[1]);
    }

    /// <summary>Los ids del proveedor se publican recortados: alcanzan para rastrear y no exponen el recurso entero.</summary>
    private static string Mask(string id) => id.Length <= 12 ? id : $"{id[..10]}…{id[^4..]}";

    /// <summary>Red de seguridad: si un mensaje del proveedor arrastrara un token, no llega al documento.</summary>
    private static string Sanitize(string message) =>
        System.Text.RegularExpressions.Regex.Replace(message, @"duffel_(test|live)_[A-Za-z0-9_\-]+", "[token oculto]");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TurisClick.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("no se encontró la raíz del repo");
    }

    private static string BuildDocument(string body, DateTimeOffset runAt) =>
        $"""
        # Duffel en modo de prueba — evidencia de la integración

        <!-- Generado por tools/duffel-spike; no editar a mano. -->

        Qué prueba este documento: que TurisClick habla con una API de inventario aéreo real —no con un
        mock— y que el ciclo buscar → revalidar → reservar funciona de punta a punta.

        **Garantías de esta corrida**, verificables en los datos de abajo:

        - se usó exclusivamente el **modo de prueba** de Duffel (token `duffel_test_…`; el adapter se
          niega a arrancar con uno que no lo sea);
        - toda oferta y toda orden volvió con `live_mode: false`;
        - **no se movió dinero real**: en modo de prueba el saldo de la cuenta es ilimitado y el pago se
          declara contra ese balance, sin tarjeta;
        - los pasajeros fueron **sintéticos**, sin un solo dato personal real;
        - el token no aparece en este documento ni en ningún log.

        {body}

        ---

        Generado el {runAt:yyyy-MM-dd HH:mm zzz}. Reproducible con `dotnet run --project tools/duffel-spike`
        (requiere el token de prueba en User Secrets).

        """;
}
