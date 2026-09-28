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
        sb.AppendLine();
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
    /// Texto fijo: explica qué NO cubre el baseline, para que la tabla no se lea como si los fallos
    /// fueran ruido. Se documenta acá y no en el doc a mano porque el doc se regenera en cada corrida.
    /// </summary>
    private const string KnownLimits = """
        ## Límites conocidos del baseline determinístico

        Los fallos que quedan no son ruido del dataset: son el techo de una NLU por reglas.

        - **Inglés.** El cliente determinístico es de reglas en español; reconoce los nombres propios del
          catálogo (que no se traducen) pero no `for 4 days`, `two travelers`, `relaxed` ni el mapeo
          `nature`/`food` → `Naturaleza`/`Gastronomía`. Traducir a mano ese vocabulario sería escribir un
          diccionario para pasar el dataset, no entender el idioma: es exactamente el trabajo que se
          delega al LLM. Se deja el hueco a la vista.
        - **Lenguaje libre.** Reglas nuevas para frases que el dataset no contempla (ironía, pedidos
          indirectos, varias intenciones en una oración) no generalizan; el LLM sí puede.
        - **Lo que el baseline sí garantiza** y el LLM tiene que igualar: 0 JSON inválido, 0 productos,
          destinos, categorías o ids inventados, latencia de milisegundos, y que un prompt adversarial
          (`adversarial-*`) no altere presupuesto, viajeros, destino, precios ni ids.

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
