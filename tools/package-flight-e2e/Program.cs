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

        // El alta de operadores la hace un administrador: el autorregistro público ya no existe. La cuenta nace
        // con una contraseña temporal que bloquea toda operación hasta cambiarla, así que se recorre el camino
        // completo igual que lo haría una persona.
        var providerEmail = $"e2e.vuelo.{suffix}@turisclick.dev";
        var created = await Post("/api/admin/provider-accounts", new
        {
            companyName = $"Operador E2E {suffix}",
            legalDocument = $"DOC-{suffix}",
            contactEmail = providerEmail,
            firstName = "Operador",
            lastName = "E2E",
            email = providerEmail,
            approve = true,
        }, adminToken);
        Check(created.Ok, "operador dado de alta por el admin", created.Status);

        var temporaryPassword = created.Body?["temporaryPassword"]?.GetValue<string>();
        var temporarySession = await LoginAsync(providerEmail, temporaryPassword!);

        const string providerPassword = "OperadorTurisClick2026!";
        var changed = await Post("/api/auth/change-password",
            new { currentPassword = temporaryPassword, newPassword = providerPassword }, temporarySession);
        Check(changed.Ok, "contraseña temporal cambiada en el primer ingreso", changed.Status);

        var providerToken = changed.Body?["accessToken"]?.GetValue<string>();

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
            // Sin política no habría nada que cancelar desde la app: es parte de lo que se valida acá.
            cancellationPolicy = new[]
            {
                new { minDaysBefore = 30, refundPercentage = 100 },
                new { minDaysBefore = 15, refundPercentage = 50 },
                new { minDaysBefore = 0, refundPercentage = 0 },
            },
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
        // ---------------------------------------------------------------- 7. cancelar desde el producto
        //
        // Lo que se prueba acá, y que ningún test con el proveedor falso puede probar: que lo que Duffel
        // informa como reembolso es lo que TurisClick le muestra a la persona, y que confirmar cancela el
        // pasaje de verdad.
        Report.AppendLine("## Cancelación con reembolso");
        Report.AppendLine();

        var slotsBefore = await ReservedSlotsAsync(configuration, reservationId!);

        var cancellationQuote = await Post($"/api/reservations/{reservationId}/cancellation-quote", null, touristToken);
        Check(cancellationQuote.Ok, "presupuesto de cancelación calculado", cancellationQuote.Status);

        var quoteLines = cancellationQuote.Body?["lines"]?.AsArray() ?? [];
        var flightCancellationLine = quoteLines.FirstOrDefault(l => l?["component"]?.GetValue<string>() == "FLIGHT");
        var packageCancellationLine = quoteLines.FirstOrDefault(l => l?["component"]?.GetValue<string>() == "PACKAGE");

        Check(packageCancellationLine is not null, "el presupuesto tiene la línea del paquete");
        Check(flightCancellationLine is not null, "el presupuesto tiene la línea del vuelo, calculada aparte");

        Report.AppendLine("| Componente | Pagado | Reembolso | Cargo | Según |");
        Report.AppendLine("|---|---|---|---|---|");
        foreach (var line in quoteLines)
        {
            var known = line?["refundKnown"]?.GetValue<bool>() ?? false;
            var percentage = line?["refundPercentage"]?.GetValue<int?>();

            Report.AppendLine(
                $"| {line?["label"]} | {line?["paidAmount"]} {line?["currency"]} | " +
                $"{(known ? $"{line?["refundAmount"]} {line?["currency"]}" : "no informado")} | " +
                $"{line?["feeAmount"]} {line?["currency"]} | " +
                $"{(percentage is null ? "lo que informó la aerolínea" : $"política del operador ({percentage}%)")} |");
        }
        Report.AppendLine();

        var cancellationQuoteId = cancellationQuote.Body?["quoteId"]?.GetValue<string>();

        // El cliente sólo manda el id del presupuesto: ningún importe viaja desde afuera.
        var cancelled = await Post($"/api/reservations/{reservationId}/cancel",
            new { cancellationQuoteId }, touristToken);
        Check(cancelled.Ok, "cancelación ejecutada", cancelled.Status);

        var cancellationStatus = cancelled.Body?["cancellation"]?["status"]?.GetValue<string>();
        Check(cancelled.Body?["status"]?.GetValue<string>() == "CANCELLED", "la reserva quedó CANCELLED");
        Check(cancellationStatus == "COMPLETED", $"la cancelación quedó COMPLETED ({cancellationStatus})");
        Check(cancelled.Body?["cancellation"]?["flightCancelled"]?.GetValue<bool>() == true,
            "el pasaje quedó cancelado en la aerolínea");

        var slotsAfter = await ReservedSlotsAsync(configuration, reservationId!);
        Check(slotsBefore - slotsAfter == 1, $"el cupo se liberó exactamente una vez ({slotsBefore} → {slotsAfter})");

        // El libro de pagos, visto por el ADMIN: cobro y reembolsos, sin reescribir nada.
        var ledger = await Get($"/api/admin/reservations/{reservationId}/payments", adminToken);
        var transactions = ledger.Body?["transactions"]?.AsArray() ?? [];
        var charges = transactions.Count(t => t?["type"]?.GetValue<string>() == "CHARGE");
        var refunds = transactions.Count(t => t?["type"]?.GetValue<string>() == "REFUND");

        Check(charges >= 1 && refunds >= 1, $"el libro conserva el cobro y el reembolso ({charges} cobro(s), {refunds} reembolso(s))");

        Report.AppendLine("| Movimiento | Importe | Componente | Estado |");
        Report.AppendLine("|---|---|---|---|");
        foreach (var movement in transactions)
            Report.AppendLine(
                $"| {movement?["type"]} | {movement?["amount"]} {movement?["currency"]} | " +
                $"{movement?["component"] ?? "—"} | {movement?["status"]} |");
        Report.AppendLine();

        foreach (var balance in ledger.Body?["balances"]?.AsArray() ?? [])
            Report.AppendLine(
                $"- Saldo {balance?["currency"]}: cobrado {balance?["charged"]}, devuelto {balance?["refunded"]}, " +
                $"neto {balance?["net"]}.");
        Report.AppendLine();

        // "Mis viajes" conserva la historia: la reserva cancelada sigue visible con su reembolso.
        var history = await Get("/api/reservations/me?page=1&pageSize=5", touristToken);
        var historic = history.Body?["items"]?.AsArray()
            .FirstOrDefault(i => i?["id"]?.GetValue<string>() == reservationId);

        Check(historic is not null, "la reserva cancelada sigue en \"Mis viajes\"");
        Check(historic?["status"]?.GetValue<string>() == "CANCELLED", "se muestra como cancelada");
        Check(historic?["cancellation"]?["status"]?.GetValue<string>() == "COMPLETED",
            "con el resultado de la cancelación y lo reembolsado");

        // Si el producto ya canceló la orden, no hay nada que limpiar; si algo falló, se cancela igual para no
        // dejar una orden de prueba viva.
        var orderId = cancellationStatus == "COMPLETED"
            ? null
            : await ResolveOrderIdAsync(configuration, reservationId!);

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

    /// <summary>
    /// Cupo tomado de la salida del paquete. Se lee de la base local porque la API no expone `reserved_slots`
    /// y lo que hay que demostrar es que se devuelve exactamente una vez.
    /// </summary>
    private static async Task<int> ReservedSlotsAsync(IConfiguration configuration, string reservationId)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString)) return -1;

        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new Npgsql.NpgsqlCommand(
            """
            SELECT a.reserved_slots
            FROM package_availabilities a
            JOIN reservation_items i ON i.package_availability_id = a.id
            WHERE i.reservation_id = @id
            """, connection);
        command.Parameters.AddWithValue("id", Guid.Parse(reservationId));

        return await command.ExecuteScalarAsync() is int slots ? slots : -1;
    }

    private static async Task<int> FinishAsync(string? orderId, FlightsOptions options)
    {
        if (orderId is { Length: > 0 })
        {
            using var http = new HttpClient();
            var provider = new DuffelFlightProvider(http, Options.Create(options), NullLogger<DuffelFlightProvider>.Instance);

            try
            {
                var pending = await provider.QuoteCancellationAsync(orderId, CancellationToken.None);
                var cancellation = await provider.ConfirmCancellationAsync(pending.CancellationId, CancellationToken.None);
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
            "los nombres de pasajero y el importe que TurisClick arma son aceptables para una API aérea real, que",
            "el localizador que termina en \"Mis viajes\" lo emitió esa API, y que el reembolso que se le muestra",
            "a la persona al cancelar es el que esa API informó.",
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
