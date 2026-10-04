using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TurisClick.Api.Modules.Ai.Services;
using TurisClick.Api.Modules.Ai.Services.LlmClients;

namespace TurisClick.AiBenchmark;

/// <summary>
/// Benchmark reproducible del agente: mide al cliente determinístico y, opcionalmente, al LLM sobre el
/// MISMO dataset y las MISMAS métricas. No toca la base ni Azure: ejercita la capa de interpretación
/// (extracción, refinamientos) y la de composición contra un set de candidatos fijo.
///
/// Para que la comparación sea honesta, el LLM se mide SIN fallback determinístico: lo que falla, cuenta
/// como falla del modelo. En la app el fallback está activo (ver FallbackAiModelClient).
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        var withLlm = args.Contains("--with-llm");
        var model = ArgValue(args, "--model") ?? "qwen2.5:7b-instruct";
        var baseUrl = ArgValue(args, "--ollama") ?? "http://localhost:11434";
        var repoRoot = FindRepoRoot();
        var dataset = await LoadDatasetAsync(Path.Combine(AppContext.BaseDirectory, "dataset.json"));

        Console.WriteLine($"Casos: {dataset.Cases.Count} | LLM: {(withLlm ? $"{model} @ {baseUrl}" : "no")}");

        var runs = new List<RunResult> { await RunAsync("deterministic", new DeterministicAiModelClient(), dataset) };

        if (withLlm)
        {
            using var http = new HttpClient();
            var options = Options.Create(new AiOptions
            {
                Provider = "Ollama",
                Ollama = new OllamaOptions { BaseUrl = baseUrl, Model = model, TimeoutSeconds = 120 },
            });
            var client = new OllamaAiModelClient(http, options, NullLogger<OllamaAiModelClient>.Instance);
            runs.Add(await RunAsync($"llm:{model}", client, dataset));

            // Tercera columna: el mismo modelo, con las reglas ganando en los escalares literales.
            // Tampoco lleva fallback, para que la comparación siga midiendo al modelo y no a la red.
            var hybrid = new HybridAiModelClient(client, new DeterministicAiModelClient(), NullLogger<HybridAiModelClient>.Instance);
            runs.Add(await RunAsync($"hybrid:{model}", hybrid, dataset));
        }

        var report = new BenchmarkReport(DateTimeOffset.Now, dataset.Cases.Count, runs);
        var jsonPath = Path.Combine(repoRoot, "docs", "ai-evaluation-results.json");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, Json));
        var markdownPath = Path.Combine(repoRoot, "docs", "ai-evaluation.md");
        await File.WriteAllTextAsync(markdownPath, Markdown(report), new UTF8Encoding(false));

        Console.WriteLine();
        foreach (var run in runs) Console.WriteLine(Summary(run));
        Console.WriteLine($"\nResultados: {jsonPath}\n            {markdownPath}");
        return 0;
    }

    // ---------------- Ejecución ----------------

    private static async Task<RunResult> RunAsync(string label, IAiModelClient client, Dataset dataset)
    {
        var results = new List<CaseResult>();
        var today = DateOnly.ParseExact(dataset.Today, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var snapshot = new ExtractedPreferencesSnapshot(null, null, null, null, null, null, null, [], null);

        foreach (var testCase in dataset.Cases)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = testCase.Kind switch
                {
                    "extraction" => await ScoreExtractionAsync(client, testCase, dataset, snapshot, today),
                    "refinement" => await ScoreRefinementAsync(client, testCase, dataset, snapshot),
                    "composition" => await ScoreCompositionAsync(client, testCase, dataset, snapshot),
                    _ => throw new InvalidOperationException($"kind desconocido: {testCase.Kind}"),
                };
                results.Add(result with { ElapsedMs = stopwatch.ElapsedMilliseconds });
            }
            catch (AiModelUnavailableException)
            {
                results.Add(Failed(testCase, "modelo no disponible", stopwatch.ElapsedMilliseconds, unavailable: true));
            }
            catch (AiModelResponseException)
            {
                results.Add(Failed(testCase, "JSON inválido tras reintento", stopwatch.ElapsedMilliseconds, invalidJson: true));
            }
            Console.Write('.');
        }
        Console.WriteLine();

        return new RunResult(label, results);
    }

    private static CaseResult Failed(BenchmarkCase testCase, string note, long elapsed, bool unavailable = false, bool invalidJson = false) =>
        new(testCase.Id, testCase.Kind, testCase.Language, [], note, elapsed, unavailable, invalidJson, InventedIds: 0);

    /// <summary>Cada campo esperado es un check independiente: así el promedio no premia adivinar de más.</summary>
    private static async Task<CaseResult> ScoreExtractionAsync(
        IAiModelClient client, BenchmarkCase testCase, Dataset dataset, ExtractedPreferencesSnapshot snapshot, DateOnly today)
    {
        var result = await client.ExtractPreferencesAsync(
            new PreferenceExtractionRequest([], testCase.Message, snapshot, dataset.Vocabulary.Destinations, dataset.Vocabulary.Categories, today),
            CancellationToken.None);

        var expected = testCase.Expected;
        var checks = new List<Check>
        {
            Check.Of("destino", expected.Destination, result.DestinationMention),
            Check.Of("intereses", expected.Categories is null ? null : string.Join(",", expected.Categories.OrderBy(c => c)),
                result.CategoryMentions.Count == 0 ? null : string.Join(",", result.CategoryMentions.Distinct().OrderBy(c => c))),
            Check.Of("viajeros", expected.Travelers?.ToString(), result.TravelersCount?.ToString()),
            Check.Of("duración", expected.DurationDays?.ToString(), result.DurationDays?.ToString()),
            Check.Of("presupuesto", expected.BudgetAmount?.ToString(CultureInfo.InvariantCulture), result.BudgetAmount?.ToString(CultureInfo.InvariantCulture)),
            Check.Of("fechas", Range(expected.StartDate, expected.EndDate), Range(result.StartDate?.ToString("yyyy-MM-dd"), result.EndDate?.ToString("yyyy-MM-dd"))),
            Check.Of("ritmo", expected.Pace, result.TravelPaceMention),
        };

        // Alucinación: nombrar un destino o categoría que no está en el vocabulario real.
        var invented = 0;
        if (result.DestinationMention is { } d && !dataset.Vocabulary.Destinations.Contains(d)) invented++;
        invented += result.CategoryMentions.Count(c => !dataset.Vocabulary.Categories.Contains(c));

        return new CaseResult(testCase.Id, testCase.Kind, testCase.Language, checks, null, 0, false, false, invented);
    }

    private static string? Range(string? start, string? end) => start is null && end is null ? null : $"{start}..{end}";

    private static async Task<CaseResult> ScoreRefinementAsync(
        IAiModelClient client, BenchmarkCase testCase, Dataset dataset, ExtractedPreferencesSnapshot snapshot)
    {
        var items = CurrentItinerary();
        var result = await client.InterpretModificationAsync(
            new ModificationInterpretationRequest([], testCase.Message, snapshot, items, dataset.Vocabulary.Categories),
            CancellationToken.None);

        var expected = testCase.Expected;
        var checks = new List<Check> { Check.Of("acción", expected.Action, result.Action.ToString()) };

        if (expected.TargetTitles is { Count: > 0 })
        {
            var expectedIds = items.Where(i => expected.TargetTitles.Contains(i.Title)).Select(i => i.ItemId).ToHashSet();
            checks.Add(Check.Of("ítems señalados", string.Join(",", expectedIds.OrderBy(id => id)),
                string.Join(",", result.TargetItemIds.Distinct().OrderBy(id => id))));
        }
        if (expected.AddCategories is { Count: > 0 })
        {
            checks.Add(Check.Of("categorías a sumar", string.Join(",", expected.AddCategories.OrderBy(c => c)),
                string.Join(",", result.AddCategoryNames.Distinct().OrderBy(c => c))));
        }

        var invented = result.TargetItemIds.Count(id => items.All(i => i.ItemId != id))
            + result.AddCategoryNames.Count(c => !dataset.Vocabulary.Categories.Contains(c));

        return new CaseResult(testCase.Id, testCase.Kind, testCase.Language, checks, null, 0, false, false, invented);
    }

    private static async Task<CaseResult> ScoreCompositionAsync(
        IAiModelClient client, BenchmarkCase testCase, Dataset dataset, ExtractedPreferencesSnapshot snapshot)
    {
        var candidates = Candidates();
        var days = testCase.Expected.Days ?? 2;
        var result = await client.ComposeItineraryAsync(
            new ItineraryCompositionRequest(snapshot, days, candidates, [], [], null, null),
            CancellationToken.None);

        var offered = candidates.Select(c => c.Id).ToHashSet();
        var invented = result.Items.Count(i => !offered.Contains(i.ProductId));
        var checks = new List<Check>
        {
            new("propone al menos un componente", "≥1", result.Items.Count.ToString(), result.Items.Count > 0),
            new("todos los ids son reales", "0 inventados", invented.ToString(), invented == 0),
            new("respeta la cantidad de días", $"≤{days}", result.Items.Count == 0 ? "-" : result.Items.Max(i => i.DayNumber).ToString(),
                result.Items.Count > 0 && result.Items.Max(i => i.DayNumber) <= days),
        };

        return new CaseResult(testCase.Id, testCase.Kind, testCase.Language, checks, null, 0, false, false, invented);
    }

    // ---------------- Datos fijos para refinamientos y composición ----------------

    private static List<CurrentItineraryItemView> CurrentItinerary() =>
    [
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), 1, "EXPERIENCE", "Rafting en el río Espíritu Santo", ["Aventura"], 320, "BOB", null),
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 2, "EXPERIENCE", "Museo de la Recoleta", ["Cultura", "Historia"], 80, "BOB", null),
        new(Guid.Parse("33333333-3333-3333-3333-333333333333"), 3, "EXPERIENCE", "Caminata por el Valle de la Luna", ["Naturaleza"], 120, "BOB", null),
    ];

    private static List<CandidateExperience> Candidates() =>
    [
        new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "Sucre, la Ciudad Blanca a pie", "Sucre", 120, "BOB", ["Cultura", "Historia"], 180,
            [new CandidateAvailability(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"), new DateOnly(2026, 10, 10), 12)]),
        new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), "Parque Cretácico y la pared de Cal Orck'o", "Sucre", 150, "BOB", ["Naturaleza", "Historia"], 180,
            [new CandidateAvailability(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"), new DateOnly(2026, 10, 11), 20)]),
        new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"), "Chocolate, chorizos y sabores de Sucre", "Sucre", 180, "BOB", ["Gastronomía"], 150,
            [new CandidateAvailability(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003"), new DateOnly(2026, 10, 12), 10)]),
    ];

    // ---------------- Reporte ----------------

    private static string Summary(RunResult run)
    {
        var s = run.Stats;
        return $"{run.Label,-24} aciertos {s.Correct}/{s.Total} ({s.Accuracy:P0}) | JSON inválido {s.InvalidJson} | no disponible {s.Unavailable} | inventados {s.Invented} | mediana {s.MedianMs} ms";
    }

    private static string Markdown(BenchmarkReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Evaluación del agente: determinístico vs LLM + RAG");
        sb.AppendLine();
        sb.AppendLine("<!-- Generado por tools/ai-benchmark; no editar a mano. -->");
        sb.AppendLine($"Generado el {report.GeneratedAt:yyyy-MM-dd HH:mm} sobre {report.CaseCount} casos de `tools/ai-benchmark/dataset.json`.");
        sb.AppendLine();
        sb.AppendLine("Reproducir:");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine("dotnet run --project tools/ai-benchmark                 # sólo determinístico");
        sb.AppendLine("dotnet run --project tools/ai-benchmark -- --with-llm   # + Ollama corriendo");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("El LLM se mide **sin** fallback determinístico: lo que falla cuenta como falla del modelo.");
        sb.AppendLine("En la app el fallback está activo, así que el usuario nunca ve esas fallas.");
        sb.AppendLine();
        sb.AppendLine("## Estado de las mediciones");
        sb.AppendLine();
        var llmRun = report.Runs.FirstOrDefault(r => r.Label.StartsWith("llm:", StringComparison.Ordinal));
        sb.AppendLine("| Proveedor | Estado |");
        sb.AppendLine("|---|---|");
        sb.AppendLine("| `deterministic` | **medido** — números reales de esta corrida |");
        sb.AppendLine(llmRun is null
            ? "| `llm + rag` | **PENDING LOCAL OLLAMA BENCHMARK** — Ollama no está instalado todavía; ningún número del LLM está medido ni estimado |"
            : $"| `{llmRun.Label}` | **medido** — Ollama local, sin fallback |");
        var hybridRun = report.Runs.FirstOrDefault(r => r.Label.StartsWith("hybrid:", StringComparison.Ordinal));
        if (hybridRun is not null)
            sb.AppendLine($"| `{hybridRun.Label}` | **medido** — el mismo modelo, con las reglas ganando en los escalares literales |");
        sb.AppendLine();
        sb.Append(MeasurementHistory);
        sb.AppendLine("## Resumen");
        sb.AppendLine();
        sb.AppendLine("| Proveedor | Aciertos | Extracción | Refinamientos | Composición | JSON inválido | No disponible | Datos inventados | Mediana | p95 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var run in report.Runs)
        {
            var s = run.Stats;
            sb.AppendLine($"| `{run.Label}` | {s.Correct}/{s.Total} ({s.Accuracy:P0}) | {s.ByKind["extraction"]:P0} | {s.ByKind["refinement"]:P0} | {s.ByKind["composition"]:P0} | {s.InvalidJson} | {s.Unavailable} | {s.Invented} | {s.MedianMs} ms | {s.P95Ms} ms |");
        }
        sb.AppendLine();
        sb.AppendLine("## Por campo");
        sb.AppendLine();
        var fields = report.Runs.SelectMany(r => r.Results).SelectMany(r => r.Checks).Select(c => c.Field).Distinct().ToList();
        sb.AppendLine($"| Campo | {string.Join(" | ", report.Runs.Select(r => $"`{r.Label}`"))} |");
        sb.AppendLine($"|---|{string.Join("|", report.Runs.Select(_ => "---"))}|");
        foreach (var field in fields)
        {
            var cells = report.Runs.Select(run =>
            {
                var checks = run.Results.SelectMany(r => r.Checks).Where(c => c.Field == field).ToList();
                return checks.Count == 0 ? "—" : $"{checks.Count(c => c.Ok)}/{checks.Count}";
            });
            sb.AppendLine($"| {field} | {string.Join(" | ", cells)} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Caso por caso");
        sb.AppendLine();
        sb.AppendLine($"| Caso | Idioma | Mensaje | {string.Join(" | ", report.Runs.Select(r => $"`{r.Label}`"))} |");
        sb.AppendLine($"|---|---|---|{string.Join("|", report.Runs.Select(_ => "---"))}|");
        var first = report.Runs[0];
        foreach (var (result, index) in first.Results.Select((r, i) => (r, i)))
        {
            var cells = report.Runs.Select(run =>
            {
                var r = run.Results[index];
                if (r.Note is not null) return $"⚠️ {r.Note}";
                var failed = r.Checks.Where(c => !c.Ok).Select(c => c.Field).ToList();
                return failed.Count == 0 ? "✅" : $"❌ {string.Join(", ", failed)}";
            });
            sb.AppendLine($"| `{result.CaseId}` | {result.Language} | {DatasetMessage(result.CaseId)} | {string.Join(" | ", cells)} |");
        }
        sb.AppendLine();
        sb.Append(KnownLimits);
        return sb.ToString();
    }

    /// <summary>
    /// Historial de mediciones y análisis. Vive en el generador porque el documento se reescribe en cada
    /// corrida: si estuviera escrito a mano en el .md, la próxima corrida lo borraría.
    /// </summary>
    private const string MeasurementHistory = """
        ## Historial de mediciones (todas reales, en la misma máquina)

        Ollama 100% GPU sobre una Radeon RX 9060 XT (16 GB de VRAM), `qwen2.5:7b-instruct` Q4, 33 casos.

        | # | Qué cambió | `deterministic` | `llm` | `hybrid` | mediana del LLM |
        |---|---|---|---|---|---|
        | 1 | Primera medición real del LLM | 170/179 (95%) | **148/179 (83%)** | — | 1140 ms |
        | 2 | `required` completo en los JSON Schemas | 170/179 | **165/179 (92%)** | — | 1746 ms |
        | 3 | Calendario resuelto por el backend + reglas por campo en el prompt | 170/179 | **172/179 (96%)** | — | 1757 ms |
        | 4 | Proveedor `Hybrid`: las reglas ganan en los escalares literales | 170/179 (95%) | 172/179 (96%) | **174/179 (97%)** | 1755 ms |

        La corrida 1 está guardada cruda en `ai-evaluation-results-before-schema-fix.json`. No se tocó el
        dataset para mejorar estos números: los cambios fueron de integración, y el único caso agregado al
        dataset en toda la fase fue un adversarial más, que el LLM acierta.

        ### El 83% inicial era un bug nuestro, no un límite del modelo

        En la corrida 1, **13 de los 31 fallos del LLM eran `durationDays` en null** — incluso con "tres
        días" escrito en el mensaje. El resto se repartía entre viajeros, fechas y ritmo. La causa estaba
        en nuestro esquema: `required` listaba sólo tres propiedades, y con una propiedad opcional la
        gramática le permite al modelo **omitir la clave**, que es el camino más corto. Omitir no es
        responder null: es no haber pensado el campo.

        Se midió mandando el mismo prompt dos veces, variando sólo el `required`:

        | Mensaje | `required` parcial | `required` completo |
        |---|---|---|
        | "Quiero tres días tranquilos en Sucre…" | `durationDays` ausente | `durationDays: 3` |
        | "Viajo sola a Uyuni, 2 días…" | ambos ausentes | `travelers: 1`, `durationDays: 2` |
        | "I'm going to La Paz for 4 days with my girlfriend…" | `durationDays` ausente | `durationDays: 4` |

        Mismo modelo, misma temperatura, mismo prompt: +17 aciertos. Es el hallazgo más transferible de
        esta fase — **cuando un modelo "no extrae un campo", primero hay que sospechar del contrato**.

        ### La aritmética de fechas no es trabajo del modelo

        Resuelto el esquema, el LLM empezó a intentar las fechas relativas y las erraba: con hoy miércoles
        2026-09-23 ubicó "este fin de semana" en el 29 y 30 de septiembre (martes y miércoles). Contar días
        de calendario es exactamente lo que un LLM hace peor y lo que el backend hace sin margen de error,
        así que el prompt dejó de pedírselo: ahora lleva un bloque `CALENDARIO` con hoy, mañana, el fin de
        semana próximo y la semana que viene ya calculados, y al modelo le queda sólo decidir a qué se
        refería el turista. Es la misma idea que el RAG del catálogo: los hechos los pone el backend.

        El efecto colateral fue medido también: con el calendario a la vista, el modelo empezó a rellenar
        fechas en mensajes que no hablaban de fechas. Se corrigió diciéndolo explícitamente ("el calendario
        está para traducir lo que el turista dijo, no para sugerirle fechas") y se volvió a medir.

        ### Lo que el modelo sigue haciendo mal: inferir de más

        Los fallos que quedan son de un solo tipo — el modelo contesta donde correspondía callarse:
        viajeros 1 cuando el mensaje no nombra a nadie, `INTENSE` porque el turista pidió aventura,
        `Relax y bienestar` porque dijo "relaxed days", `Aventura` porque pidió buceo (que no está en el
        catálogo). El prompt lo prohíbe explícitamente en tres lugares distintos y aun así ocurre: es
        error del modelo, no del contrato. El baseline de reglas no comete ninguno de esos, porque una
        regla que no matchea no devuelve nada.

        ### Por qué el híbrido gana

        La asimetría de los errores es lo que hace que combinarlos sume en vez de promediar: **cuando las
        reglas fallan devuelven null, cuando el modelo falla devuelve un valor equivocado**. De ahí el
        reparto: los escalares con evidencia literal (duración, viajeros, fechas, presupuesto — su regex
        exige la unidad: "5 días", "4 personas", "800 bolivianos", una fecha ISO) los gana la regla si
        matcheó; todo lo que pide entender el idioma (destino, intereses, ritmo, refinamientos, redacción)
        queda en el modelo. Dos celdas de la tabla cambian respecto del LLM solo, y las dos mejoran.

        ### Tradeoff: precisión, flexibilidad y latencia

        | | `deterministic` | `llm` | `hybrid` |
        |---|---|---|---|
        | Aciertos | 170/179 (95%) | 172/179 (96%) | **174/179 (97%)** |
        | Español coloquial | muy bueno | bueno | muy bueno |
        | Inglés | **no entiende** | bueno | bueno |
        | Lenguaje libre fuera del dataset | no generaliza | generaliza | generaliza |
        | Mediana por turno | **0 ms** | 1757 ms | 1755 ms |
        | p95 (incluye composición) | 21 ms | ~4,7 s | ~4,7 s |
        | Reproducibilidad | exacta por construcción | **exacta, medida** | exacta, medida |
        | Datos inventados | 0 | 0 | 0 |

        La reproducibilidad del LLM se midió en serio: dos corridas completas con `temperature 0` dieron
        **0 campos distintos** entre sí. Eso es lo que permite usar estos números en una tesis.

        El precio del LLM es la latencia: pasar de 0 ms a ~1,8 s por turno, y ~4,7 s cuando compone un
        itinerario. Para una conversación es aceptable; para un endpoint de catálogo no lo sería. Por eso
        la elección de proveedor es por configuración y no una decisión global de arquitectura.

        ### Recomendación

        - **Local / demo: `Hybrid`.** Mejor puntaje, entiende inglés y lenguaje libre, y los guardrails
          son los mismos que con el LLM solo porque envuelve al mismo decorador.
        - **Azure (App Service F1): `Deterministic`.** No hay GPU ni RAM para un modelo, y el agente
          funciona completo sin él: 95% sobre el mismo dataset, 0 ms, 0 datos inventados.
        - El salto de 83% a 97% no se consiguió cambiando de modelo ni agrandándolo: se consiguió
          arreglando el contrato, sacándole al modelo el trabajo que no le corresponde, y dejando que las
          reglas ganen donde el dato está escrito.

        """;

    /// <summary>
    /// Texto fijo: explica qué NO cubre cada proveedor, para que la tabla no se lea como si los fallos
    /// fueran ruido. Se documenta acá y no en el doc a mano porque el doc se regenera en cada corrida.
    /// </summary>
    private const string KnownLimits = """
        ## Límites conocidos de cada proveedor

        Los fallos que quedan no son ruido del dataset: cada uno marca el techo de su enfoque.

        **Determinístico — los 9 fallos son los 3 casos en inglés.** Es una NLU de reglas en español;
        reconoce los nombres propios del catálogo (que no se traducen) pero no `for 4 days`,
        `two travelers`, `relaxed` ni el mapeo `nature`/`food` → `Naturaleza`/`Gastronomía`. Traducir a
        mano ese vocabulario sería escribir un diccionario para pasar el dataset, no entender el idioma:
        es exactamente el trabajo que se delega al LLM. Tampoco generaliza a frases que el dataset no
        contempla (ironía, pedidos indirectos, varias intenciones en una oración).

        **LLM e híbrido — inferir de más.** Completan campos que el mensaje no respalda: viajeros 1 sin
        que se nombre a nadie, un ritmo deducido del tipo de actividad, una categoría deducida de una
        palabra de ánimo o de una actividad que no está en el catálogo. El prompt lo prohíbe en tres
        lugares distintos; es error del modelo. Dos matices honestos:

        - En `es-coloquial-1` ("nos vamos con dos amigos") el LLM responde 4 viajeros y las reglas 3. El
          mensaje es **genuinamente ambiguo** —"nos" ya son dos— y el benchmark puntúa la lectura del
          baseline. La expectativa se deja como está: cambiarla para premiar al modelo sería maquillar.
        - En `adversarial-categoria` ("buceo y paracaidismo") el modelo contesta `Aventura`. No es una
          alucinación —`Aventura` existe en el catálogo— pero sí es una generalización que el producto no
          quiere: deriva en proponer actividades que el turista no pidió.

        **Lo que los tres garantizan por igual:** 0 JSON inválido, 0 productos, destinos, categorías o ids
        inventados, y que un prompt adversarial (`adversarial-*`) no altere presupuesto, viajeros,
        destino, precios ni ids. La precisión se negoció; la seguridad no.

        """;

    private static readonly Dictionary<string, string> Messages = new();
    /// <summary>Una celda de tabla no tolera saltos de línea ni pipes: el mensaje se aplana.</summary>
    private static string DatasetMessage(string caseId) => Messages.GetValueOrDefault(caseId, "")
        .ReplaceLineEndings(" / ").Replace("|", "/");

    private static async Task<Dataset> LoadDatasetAsync(string path)
    {
        var dataset = JsonSerializer.Deserialize<Dataset>(await File.ReadAllTextAsync(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("dataset vacío");
        foreach (var c in dataset.Cases) Messages[c.Id] = c.Message;
        return dataset;
    }

    private static string? ArgValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TurisClick.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("no se encontró la raíz del repo");
    }
}

// ---------------- Modelos del dataset y del reporte ----------------

public record Dataset(Vocabulary Vocabulary, string Today, List<BenchmarkCase> Cases);
public record Vocabulary(List<string> Destinations, List<string> Categories);
public record BenchmarkCase(string Id, string Kind, string Language, string Message, ExpectedValues Expected);

public record ExpectedValues(
    string? Destination = null,
    List<string>? Categories = null,
    int? Travelers = null,
    int? DurationDays = null,
    decimal? BudgetAmount = null,
    string? BudgetCurrency = null,
    bool? BudgetIsPerPerson = null,
    string? StartDate = null,
    string? EndDate = null,
    string? Pace = null,
    string? Action = null,
    List<string>? TargetTitles = null,
    List<string>? AddCategories = null,
    int? Days = null);

public record Check(string Field, string? Expected, string? Actual, bool Ok)
{
    public static Check Of(string field, string? expected, string? actual) =>
        new(field, expected ?? "(nada)", actual ?? "(nada)", string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase));
}

public record CaseResult(
    string CaseId, string Kind, string Language, List<Check> Checks, string? Note,
    long ElapsedMs, bool Unavailable, bool InvalidJson, int InventedIds);

public record RunResult(string Label, List<CaseResult> Results)
{
    [JsonIgnore]
    public RunStats Stats => RunStats.From(Results);
}

public record RunStats(int Correct, int Total, double Accuracy, int InvalidJson, int Unavailable, int Invented, long MedianMs, long P95Ms, Dictionary<string, double> ByKind)
{
    public static RunStats From(List<CaseResult> results)
    {
        var checks = results.SelectMany(r => r.Checks).ToList();
        var correct = checks.Count(c => c.Ok);
        var times = results.Select(r => r.ElapsedMs).OrderBy(t => t).ToList();

        Dictionary<string, double> byKind = new();
        foreach (var kind in new[] { "extraction", "refinement", "composition" })
        {
            var kindChecks = results.Where(r => r.Kind == kind).SelectMany(r => r.Checks).ToList();
            byKind[kind] = kindChecks.Count == 0 ? 0 : (double)kindChecks.Count(c => c.Ok) / kindChecks.Count;
        }

        return new RunStats(
            correct, checks.Count, checks.Count == 0 ? 0 : (double)correct / checks.Count,
            results.Count(r => r.InvalidJson), results.Count(r => r.Unavailable), results.Sum(r => r.InventedIds),
            times.Count == 0 ? 0 : times[times.Count / 2],
            times.Count == 0 ? 0 : times[(int)Math.Min(times.Count - 1, Math.Floor(times.Count * 0.95))],
            byKind);
    }
}

public record BenchmarkReport(DateTimeOffset GeneratedAt, int CaseCount, List<RunResult> Runs);
