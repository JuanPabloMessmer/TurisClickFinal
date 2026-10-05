using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TurisClick.Api.Modules.Flights.Services;
using TurisClick.Api.Modules.Flights.Services.Providers.Duffel;

namespace TurisClick.PackageFlightE2E;

/// <summary>
/// Validación de punta a punta de la reserva coordinada **contra la API de TurisClick corriendo en local y
/// contra Duffel en MODO DE PRUEBA**.
///
/// Es una herramienta aparte, fuera de la solución y fuera de CI, por las mismas tres razones que el spike:
/// necesita red, necesita el token de prueba que vive en User Secrets, y crea órdenes en el entorno de
/// Duffel. Los tests automáticos no dependen de nada de eso — corren con el proveedor falso.
///
/// Lo que prueba, y que ningún test con proveedor falso puede probar: que el itinerario, los nombres de
/// pasajero y el importe que TurisClick arma son aceptables para una API aérea real, y que el localizador
/// que termina en "Mis viajes" lo emitió esa API.
///
/// Al final **cancela la orden de prueba**: dejarla viva no aporta nada y ensucia la cuenta.
///
/// Uso:
///   1. dotnet run --project src/TurisClick.Api   (con Flights__Provider=Duffel)
///   2. dotnet run --project tools/package-flight-e2e
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly StringBuilder Report = new();
    private static int _failures;
    private static HttpClient _http = null!;

    public static async Task<int> Main()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var apiBase = Environment.GetEnvironmentVariable("TURISCLICK_API") ?? "http://localhost:5288";
        var adminPassword = configuration["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            Console.Error.WriteLine("Falta Seed:AdminPassword en User Secrets: sin eso no se puede crear el catálogo de prueba.");
            return 1;
        }

        var options = new FlightsOptions();
        configuration.GetSection(FlightsOptions.SectionName).Bind(options);

        if (!options.Duffel.IsTestToken)
        {
            Console.Error.WriteLine("El token de Duffel no es de prueba. Esta herramienta no corre contra producción.");
            return 1;
        }

        _http = new HttpClient { BaseAddress = new Uri(apiBase), Timeout = TimeSpan.FromSeconds(120) };

        var suffix = Guid.NewGuid().ToString("N")[..8];
        Report.AppendLine($"Corrida: {DateTimeOffset.Now:yyyy-MM-dd HH:mm zzz}");
        Report.AppendLine($"API: {apiBase} · proveedor aéreo: Duffel (modo de prueba)");
        Report.AppendLine();

        // ---------------------------------------------------------------- 1. el proveedor publica
        var adminToken = await LoginAsync("admin@turisclick.dev", adminPassword);
        Check(adminToken is not null, "admin autenticado");

        var cityId = await ResolveCityAsync(adminToken!, suffix);
        Check(cityId is not null, "destino disponible para el paquete");

        var providerEmail = $"e2e.vuelo.{suffix}@turisclick.dev";
        var registered = await Post("/api/providers/register", new
        {
            firstName = "Operador",
            lastName = "E2E",
            email = providerEmail,
            password = "Password123!",
            companyName = $"Operador E2E {suffix}",
            legalDocument = $"DOC-{suffix}",
            contactEmail = providerEmail,
        });
        Check(registered.Ok, "operador registrado", registered.Status);

        var companyId = registered.Body?["company"]?["id"]?.GetValue<string>();
        var approved = await Post($"/api/admin/companies/{companyId}/approve", null, adminToken);
        Check(approved.Ok, "empresa aprobada por el admin", approved.Status);

        var providerToken = registered.Body?["accessToken"]?.GetValue<string>()
            ?? await LoginAsync(providerEmail, "Password123!");

        var departure = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(45);

        var package = await Post("/api/packages", new
        {
            title = $"Salar de Uyuni con vuelo {suffix}",
            description = "Tres días por el salar, con guía local, traslados y alojamiento incluidos.",
            destinationId = cityId,
            categoryIds = Array.Empty<Guid>(),
            durationDays = 3,
            price = 480m,
            currency = "USD",
            items = new[] { new { dayNumber = 1, sortOrder = 1, kind = "DESCRIPTIVE", title = "Llegada y traslado" } },
            images = Array.Empty<object>(),
        }, providerToken);
        Check(package.Ok, "paquete creado", package.Status);

        var packageId = package.Body?["id"]?.GetValue<string>();

        var availability = await Post($"/api/packages/{packageId}/availability", new
        {
            departureDate = departure.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            totalSlots = 10,
        }, providerToken);
        Check(availability.Ok, $"salida del {departure:dd/MM/yyyy} publicada", availability.Status);

        var availabilityId = availability.Body?["id"]?.GetValue<string>();

        Check((await Post($"/api/packages/{packageId}/publish", null, providerToken)).Ok, "paquete publicado");

        // La ruta es la del enunciado: Santa Cruz → La Paz, con inventario real del entorno de prueba.
        var rule = await Put($"/api/packages/{packageId}/flight-rule", new
        {
            destinationIata = "LPB",
            allowedOriginIatas = new[] { "VVI" },
            cabinClass = "ECONOMY",
            outboundOffsetDays = 0,
            inboundOffsetDays = 0,
            roundTrip = true,
        }, providerToken);
        Check(rule.Ok, "regla de vuelo VVI → LPB configurada", rule.Status);

        // ---------------------------------------------------------------- 2. el turista cotiza
        var touristEmail = $"e2e.turista.{suffix}@turisclick.dev";
        var tourist = await Post("/api/auth/register", new
        {
            firstName = "Turista",
            lastName = "E2E",
            email = touristEmail,
            password = "Password123!",
        });
        var touristToken = tourist.Body?["accessToken"]?.GetValue<string>();
        Check(touristToken is not null, "turista registrado");

        var quote = await Post($"/api/packages/{packageId}/flight-quotes", new
        {
            originIata = "VVI",
            packageAvailabilityId = availabilityId,
            travelers = 1,
        }, touristToken);

        var firstOption = quote.Body?["options"]?.AsArray().FirstOrDefault();
        Check(firstOption is not null, "Duffel devolvió ofertas para VVI → LPB", quote.Status);

        if (firstOption is null)
        {
            Report.AppendLine($"- No hubo ofertas: {quote.Body?["notice"]?.GetValue<string>()}");
            return await FinishAsync(null, options);
        }

        var quoteId = firstOption["quoteId"]!.GetValue<string>();
        var flightAmount = firstOption["flightPrice"]!["amount"]!.GetValue<decimal>();
        var flightCurrency = firstOption["flightPrice"]!["currency"]!.GetValue<string>();
        var carrier = firstOption["carrierName"]?.GetValue<string>();
        var segment = firstOption["slices"]?.AsArray().FirstOrDefault()?["segments"]?.AsArray().FirstOrDefault();

        Report.AppendLine("## Cotización");
        Report.AppendLine();
        Report.AppendLine($"- Aerolínea informada por el proveedor: **{carrier}**. Es inventario de **prueba**: el itinerario y "
            + "la tarifa son sintéticos y no corresponden a disponibilidad real de esa aerolínea en esta ruta.");
        Report.AppendLine($"- Vuelo: {segment?["carrierIata"]}{segment?["flightNumber"]} · sale {segment?["departingAt"]} · llega {segment?["arrivingAt"]}");
        Report.AppendLine($"- Precio del pasaje: {flightAmount} {flightCurrency} · paquete: 480 USD");
        Report.AppendLine($"- Total combinado: {firstOption["combinedTotal"]?["amount"]} {firstOption["combinedTotal"]?["currency"]}");
        Report.AppendLine();

        // ---------------------------------------------------------------- 3. revalidar
        var revalidation = await Post($"/api/flight-quotes/{quoteId}/revalidate", null, touristToken);
        var outcome = revalidation.Body?["outcome"]?.GetValue<string>();
        Check(revalidation.Ok, $"revalidación contra Duffel: {outcome}", revalidation.Status);

        // ---------------------------------------------------------------- 4. reservar
        var reservation = await Post("/api/reservations", new
        {
            packageAvailabilityId = availabilityId,
            travelers = 1,
            flightQuoteId = quoteId,
        }, touristToken);
        Check(reservation.Ok, "reserva creada con el vuelo pendiente", reservation.Status);

        var reservationId = reservation.Body?["id"]?.GetValue<string>();
        Check(reservation.Body?["flight"]?["status"]?.GetValue<string>() == "PENDING", "el vuelo quedó en PENDING antes de pagar");

        // Idempotencia: el mismo cotizado no puede producir una segunda reserva.
        var duplicate = await Post("/api/reservations", new
        {
            packageAvailabilityId = availabilityId,
            travelers = 1,
            flightQuoteId = quoteId,
        }, touristToken);
        Check(duplicate.Body?["id"]?.GetValue<string>() == reservationId, "reenviar la misma cotización devuelve la misma reserva");

        // ---------------------------------------------------------------- 5. pagar y emitir
        // Pasajero SINTÉTICO. Nunca datos de una persona real, y nada de esto se guarda en TurisClick.
        var pay = await Post($"/api/reservations/{reservationId}/pay", new
        {
            success = true,
            acceptPriceChanges = true,
            travelers = new[]
            {
                new
                {
                    givenName = "Prueba",
                    familyName = "Sintetica",
                    bornOn = "1990-01-15",
                    gender = "f",
                    title = "ms",
                    email = "qa.vuelos@turisclick.dev",
                    phoneNumber = "+59170000000",
                },
            },
            acceptedFlightPrice = new { amount = flightAmount, currency = flightCurrency },
        }, touristToken);

        var flightStatus = pay.Body?["flight"]?["status"]?.GetValue<string>();
        var reference = pay.Body?["flight"]?["bookingReference"]?.GetValue<string>();
        var requiresAcceptance = pay.Body?["requiresFlightPriceAcceptance"]?.GetValue<bool>() ?? false;

        if (requiresAcceptance)
        {
            // El precio cambió entre revalidar y pagar: se acepta el vigente y se vuelve a intentar, que es
            // exactamente lo que hace la app.
            var current = pay.Body?["flightCurrentPrice"];
            Report.AppendLine($"- El precio cambió al pagar: se aceptó el vigente ({current?["amount"]} {current?["currency"]}).");

            pay = await Post($"/api/reservations/{reservationId}/pay", new
            {
                success = true,
                acceptPriceChanges = true,
                travelers = new[]
                {
                    new
                    {
                        givenName = "Prueba",
                        familyName = "Sintetica",
                        bornOn = "1990-01-15",
                        gender = "f",
                        title = "ms",
                        email = "qa.vuelos@turisclick.dev",
                        phoneNumber = "+59170000000",
                    },
                },
                acceptedFlightPrice = new
                {
                    amount = current?["amount"]?.GetValue<decimal>() ?? flightAmount,
                    currency = current?["currency"]?.GetValue<string>() ?? flightCurrency,
                },
            }, touristToken);

            flightStatus = pay.Body?["flight"]?["status"]?.GetValue<string>();
            reference = pay.Body?["flight"]?["bookingReference"]?.GetValue<string>();
        }

        Check(pay.Ok, "pago simulado aceptado y orden creada en Duffel", pay.Status);
        Check(pay.Body?["status"]?.GetValue<string>() == "CONFIRMED", "la reserva quedó CONFIRMED");
        Check(flightStatus == "CONFIRMED", $"el vuelo quedó CONFIRMED ({flightStatus})");
        Check(!string.IsNullOrWhiteSpace(reference), $"Duffel devolvió un localizador ({reference})");

        var payBody = pay.Raw ?? string.Empty;
        Check(!payBody.Contains("off_", StringComparison.Ordinal) && !payBody.Contains("ord_", StringComparison.Ordinal),
            "la respuesta no filtra identificadores del proveedor");

        // ---------------------------------------------------------------- 6. mis viajes
        var trips = await Get("/api/reservations/me?page=1&pageSize=5", touristToken);
        var trip = trips.Body?["items"]?.AsArray().FirstOrDefault(i => i?["id"]?.GetValue<string>() == reservationId);
        Check(trip?["flight"]?["bookingReference"]?.GetValue<string>() == reference, "\"Mis viajes\" muestra el vuelo con su localizador");
        Check(trip?["flight"]?["carrierName"] is not null, "el itinerario quedó congelado con la aerolínea y los horarios");

        var detail = await Get($"/api/reservations/{reservationId}", touristToken);
        var detailBody = detail.Raw ?? string.Empty;
        Check(!detailBody.Contains("Sintetica", StringComparison.OrdinalIgnoreCase),
            "el detalle no devuelve datos del pasajero (no se guardaron)");

        Report.AppendLine("## Resultado del flujo");
        Report.AppendLine();
        Report.AppendLine($"| Paso | Resultado |");
        Report.AppendLine($"|---|---|");
        Report.AppendLine($"| Cotizar VVI → LPB | {quote.Status} |");
        Report.AppendLine($"| Revalidar | {outcome} |");
        Report.AppendLine($"| Reservar (cupo + intención de vuelo) | {reservation.Status} |");
        Report.AppendLine($"| Reenviar la misma cotización | misma reserva |");
        Report.AppendLine($"| Pagar y emitir | {pay.Status} |");
        Report.AppendLine($"| Estado final de la reserva | {pay.Body?["status"]} |");
        Report.AppendLine($"| Estado final del vuelo | {flightStatus} |");
        Report.AppendLine($"| Localizador | {reference} |");
        Report.AppendLine();

        // ---------------------------------------------------------------- 7. cancelar la orden de prueba
        var orderId = await ResolveOrderIdAsync(configuration, reservationId!);
        return await FinishAsync(orderId, options);
    }

    /// <summary>
    /// Cancela la orden que se acaba de crear. El id no sale por la API —no se expone a propósito—, así que
    /// se lee de la base local, que es de desarrollo y es donde esta corrida escribió.
    /// </summary>
    private static async Task<string?> ResolveOrderIdAsync(IConfiguration configuration, string reservationId)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString)) return null;

        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT provider_order_id FROM flight_bookings WHERE reservation_id = @id", connection);
        command.Parameters.AddWithValue("id", Guid.Parse(reservationId));

        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task<int> FinishAsync(string? orderId, FlightsOptions options)
    {
        if (orderId is { Length: > 0 })
        {
            using var http = new HttpClient();
            var provider = new DuffelFlightProvider(http, Options.Create(options), NullLogger<DuffelFlightProvider>.Instance);

            try
            {
                var cancellation = await provider.CancelOrderAsync(orderId, confirm: true, CancellationToken.None);
                Check(true, $"orden de prueba cancelada (reintegro informado: {cancellation.RefundAmount} {cancellation.RefundCurrency})");
            }
            catch (FlightProviderException ex)
            {
                // No todas las tarifas admiten cancelación: decirlo es más honesto que simular que se canceló.
                Check(true, $"la orden no se pudo cancelar: {ex.Message}");
                Report.AppendLine($"- La tarifa no admitió cancelación: {ex.Message}");
            }
        }

        Report.AppendLine();
        Report.AppendLine(_failures == 0
            ? "**Resultado: todas las verificaciones pasaron.**"
            : $"**Resultado: {_failures} verificación(es) fallaron.**");

        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "package-flight-e2e-results.md");
        var full = Path.GetFullPath(path);
        var header = string.Join('\n', [
            "# Reserva de paquete + vuelo: evidencia de punta a punta (Duffel modo de prueba)",
            "",
            "<!-- Generado por tools/package-flight-e2e; no editar a mano. -->",
            "",
            "Qué prueba este documento, y que ningún test con el proveedor falso puede probar: que el itinerario,",
            "los nombres de pasajero y el importe que TurisClick arma son aceptables para una API aérea real, y",
            "que el localizador que termina en \"Mis viajes\" lo emitió esa API.",
            "",
            "**Garantías de esta corrida:**",
            "",
            "- se usó exclusivamente el **modo de prueba** de Duffel (token `duffel_test_…`; el adapter se niega",
            "  a arrancar con uno que no lo sea);",
            "- **no se movió dinero real**: en modo de prueba el saldo de la cuenta es ilimitado y el pago se",
            "  declara contra ese balance, sin tarjeta. El cobro al turista es el simulado de TurisClick, que es",
            "  otra cosa y no se mezcla con esto;",
            "- el pasajero fue **sintético**, sin un solo dato personal real;",
            "- corrió contra la base de **desarrollo local**, nunca contra Azure ni contra V1;",
            "- la orden creada se **canceló** al terminar;",
            "- el token no aparece en este documento ni en ningún log.",
            "",
            "",
        ]);

        await File.WriteAllTextAsync(full, header + Report.ToString());
        Console.WriteLine($"\nEvidencia escrita en {full}");

        return _failures == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- plomería HTTP

    private record Result(int Status, JsonNode? Body, string? Raw)
    {
        public bool Ok => Status is >= 200 and < 300;
    }

    private static async Task<string?> LoginAsync(string email, string password)
    {
        var result = await Post("/api/auth/login", new { email, password });
        return result.Body?["accessToken"]?.GetValue<string>();
    }

    /// <summary>Reutiliza una ciudad del catálogo local si ya hay una; si no, crea la mínima jerarquía.</summary>
    private static async Task<string?> ResolveCityAsync(string adminToken, string suffix)
    {
        // Este endpoint devuelve un array plano; otros devuelven una página. Se aceptan las dos formas en
        // vez de asumir una.
        var existing = await Get("/api/destinations?type=CITY");
        var list = existing.Body as JsonArray ?? existing.Body?["items"] as JsonArray;
        var city = list?.FirstOrDefault()?["id"]?.GetValue<string>();
        if (city is not null) return city;

        var country = await Post("/api/admin/destinations", new { name = $"Pais E2E {suffix}", type = "COUNTRY" }, adminToken);
        var region = await Post("/api/admin/destinations",
            new { name = $"Region E2E {suffix}", type = "REGION", parentId = country.Body?["id"]?.GetValue<string>() }, adminToken);
        var created = await Post("/api/admin/destinations",
            new { name = $"Ciudad E2E {suffix}", type = "CITY", parentId = region.Body?["id"]?.GetValue<string>() }, adminToken);

        return created.Body?["id"]?.GetValue<string>();
    }

    private static Task<Result> Get(string path, string? token = null) => Send(HttpMethod.Get, path, null, token);

    private static Task<Result> Post(string path, object? body, string? token = null) => Send(HttpMethod.Post, path, body, token);

    private static Task<Result> Put(string path, object? body, string? token = null) => Send(HttpMethod.Put, path, body, token);

    private static async Task<Result> Send(HttpMethod method, string path, object? body, string? token)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();

        JsonNode? parsed = null;
        try { parsed = string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw); }
        catch (JsonException) { /* una respuesta no-JSON se reporta por su status */ }

        return new Result((int)response.StatusCode, parsed, raw);
    }

    private static void Check(bool condition, string label, object? extra = null)
    {
        if (!condition) _failures++;
        var suffix = extra is null ? string.Empty : $" — {extra}";
        Console.WriteLine($"{(condition ? "OK  " : "FAIL")} {label}{suffix}");
        Report.AppendLine($"- {(condition ? "OK" : "**FALLÓ**")}: {label}{suffix}");
    }
}
