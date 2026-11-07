using AssistentePatinho.Core;
using AssistentePatinho.Core.Analysis;

var failures = new List<string>();
var passed = 0;
var compressor = new Asset("A1", "COMP-01", "Compressor", "Compressor", "GA30", "Utilidades", "Alta");
var compressorReserve = new Asset("A2", "COMP-02", "Compressor reserva", "Compressor", "GA30", "Utilidades", "Média");
var pump = new Asset("A3", "BOM-01", "Bomba", "Bomba", "KSB", "Utilidades", "Alta");
var current = Order("ATUAL", "A1", false, "TEMP_ALTA", "Compressor desarma por temperatura alta", "Temperatura de descarga em 112 graus");
var sameAsset = Order("H1", "A1", true, "TEMP_ALTA", "Alta temperatura no compressor", "Desarme por temperatura de descarga", ["Inspecionar radiador"]);
var sameModel = Order("H2", "A2", true, "TEMP_ALTA", "Aquecimento do compressor reserva", "Temperatura acima do normal", ["Conferir nível de óleo"]);
var unrelated = Order("H3", "A3", true, "VAZAO_BAIXA", "Bomba com baixa vazão", "Pressão abaixo do normal", ["Comparar pressões"]);
var openSimilar = Order("A2-ABERTA", "A2", false, "TEMP_ALTA", "Compressor aquecendo", "Temperatura alta");
var dataset = new CmmsDataset([compressor, compressorReserve, pump], [current, sameAsset, sameModel, unrelated, openSimilar]);
var analysis = new MaintenanceTriageEngine().Analyze(compressor, current, dataset);

Check("prioriza mesmo ativo e falha", () => analysis.SimilarCases[0].WorkOrderId == "H1");
Check("ignora ordens ainda abertas", () => analysis.SimilarCases.All(item => item.WorkOrderId != "A2-ABERTA"));
Check("conta recorrência exata", () => analysis.RecurrenceCount == 1);
Check("reaproveita verificações históricas", () => analysis.RecommendedChecks.Contains("Inspecionar radiador"));
Check("limita confiança", () => analysis.SimilarCases.All(item => item.Similarity <= 95));
Check("explica critérios da similaridade", () =>
    analysis.SimilarCases[0].MatchReasons.Contains("mesmo código de falha (+35)") &&
    analysis.SimilarCases[0].MatchReasons.Contains("mesmo ativo (+20)") &&
    analysis.SimilarCases[0].DisplaySummary.Contains(
        "Causa registrada no histórico",
        StringComparison.Ordinal));
Check("inclui evidência rastreável", () => analysis.Evidence.Any(item => item.Contains("ATUAL", StringComparison.OrdinalIgnoreCase) || item.Contains("TEMP_ALTA", StringComparison.OrdinalIgnoreCase)));
Check("aprova conjunto CMMS consistente", () => DoesNotThrow(() => CmmsDatasetValidator.ValidateOrThrow(dataset)));
Check("exporta pacote tabular Power BI rastreável", () => ExportsPowerBiPackage(dataset));
Check("bloqueia idade negativa de backlog", () => RejectsFutureBacklog(dataset));
Check("analisador CSV encontra estrutura inconsistente", DetectsCsvQualityFindings);
Check("analisador JSON preserva erro localizado", DetectsInvalidJson);
Check("analisador JSON identifica objeto vazio", DetectsEmptyJsonObject);
Check("analisador de log não transforma marcador em diagnóstico", DetectsLogMarkersWithLimitation);
Check("cache local reutiliza análise pelo SHA-256", ReusesAnalysisCache);

var unknown = Order("NOVA", "A3", false, "ELETRICA_DESCONHECIDA", "Ruído novo", "Sintoma ainda não classificado");
var exploratory = new MaintenanceTriageEngine().Analyze(pump, unknown, new CmmsDataset([pump], [unknown]));
Check("mantém fallback sem histórico", () => exploratory.SimilarCases.Count == 0 && exploratory.RecommendedChecks.Count >= 3);
Check("bloqueia ID de ordem duplicado", () => ThrowsInvalidData(() =>
    CmmsDatasetValidator.ValidateOrThrow(new CmmsDataset([compressor], [sameAsset, sameAsset]))));
Check("bloqueia vínculo com ativo inexistente", () => ThrowsInvalidData(() =>
    CmmsDatasetValidator.ValidateOrThrow(new CmmsDataset([compressor], [sameModel]))));
Check("bloqueia ordem concluída sem encerramento", () => ThrowsInvalidData(() =>
    CmmsDatasetValidator.ValidateOrThrow(new CmmsDataset([compressor], [sameAsset with { ClosedAt = null }]))));
Check("bloqueia histórico sem causa registrada", () => ThrowsInvalidData(() =>
    CmmsDatasetValidator.ValidateOrThrow(new CmmsDataset([compressor], [sameAsset with { Cause = string.Empty }]))));
Check("bloqueia análise com ativo divergente", () => ThrowsInvalidData(() =>
    new MaintenanceTriageEngine().Analyze(pump, current, dataset)));
Check("bloqueia triagem sem descrição mínima", () => ThrowsInvalidData(() =>
{
    var incomplete = current with { Description = string.Empty };
    new MaintenanceTriageEngine().Analyze(
        compressor,
        incomplete,
        dataset with
        {
            WorkOrders = dataset.WorkOrders
                .Select(order => order.Id == incomplete.Id ? incomplete : order)
                .ToArray()
        });
}));
Check("bloqueia ID de ativo duplicado", () => ThrowsInvalidData(() =>
    CmmsDatasetValidator.ValidateOrThrow(new CmmsDataset(
        [compressor, compressorReserve with { Id = compressor.Id }],
        [current]))));
Check("bloqueia tag de ativo duplicada ignorando caixa", () => ThrowsInvalidData(() =>
    CmmsDatasetValidator.ValidateOrThrow(new CmmsDataset(
        [compressor, compressorReserve with { Tag = compressor.Tag.ToLowerInvariant() }],
        [current]))));
Check("bloqueia ordem aberta com encerramento", () => ThrowsInvalidData(() =>
    CmmsDatasetValidator.ValidateOrThrow(new CmmsDataset(
        [compressor],
        [current with { ClosedAt = current.OpenedAt.AddHours(1) }]))));
Check("bloqueia encerramento anterior à abertura", () => ThrowsInvalidData(() =>
    CmmsDatasetValidator.ValidateOrThrow(new CmmsDataset(
        [compressor],
        [sameAsset with { ClosedAt = sameAsset.OpenedAt.AddMinutes(-1) }]))));
Check("bloqueia ativo com campo essencial vazio", () => ThrowsInvalidData(() =>
    CmmsDatasetValidator.ValidateOrThrow(new CmmsDataset(
        [compressor with { Name = string.Empty }],
        [current]))));
Check("bloqueia status de ordem não controlado", () => ThrowsInvalidData(() =>
    CmmsDatasetValidator.ValidateOrThrow(new CmmsDataset(
        [compressor],
        [current with { Status = "Pendente" }]))));

if (failures.Count > 0)
{
    Console.Error.WriteLine($"Falharam {failures.Count} testes: {string.Join(", ", failures)}");
    return 1;
}

Console.WriteLine($"{passed} testes do copiloto CMMS passaram.");
return 0;

void Check(string name, Func<bool> assertion)
{
    try
    {
        if (assertion()) passed++; else failures.Add(name);
    }
    catch (Exception ex)
    {
        failures.Add($"{name} ({ex.GetType().Name})");
    }
}

static WorkOrder Order(
    string id,
    string assetId,
    bool closed,
    string failureCode,
    string title,
    string description,
    IReadOnlyList<string>? checks = null) => new(
        id,
        assetId,
        new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero),
        closed ? new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero) : null,
        closed ? "Concluída" : "Aberta",
        "Alta",
        failureCode,
        title,
        description,
        closed ? "Causa registrada" : string.Empty,
        closed ? "Solução registrada" : string.Empty,
        checks ?? [],
        []);

static bool DoesNotThrow(Action action)
{
    try
    {
        action();
        return true;
    }
    catch
    {
        return false;
    }
}

static bool ThrowsInvalidData(Action action)
{
    try
    {
        action();
        return false;
    }
    catch (InvalidDataException)
    {
        return true;
    }
}

static bool ExportsPowerBiPackage(CmmsDataset dataset)
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "copiloto-confiabilidade-bi-tests",
        Guid.NewGuid().ToString("N"));
    try
    {
        var result = CmmsBiExporter.ExportAsync(
                directory,
                dataset,
                new CmmsBiExportOptions(
                    "test-run",
                    "fixture.json",
                    new string('a', 64),
                    new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero)))
            .GetAwaiter()
            .GetResult();
        var expectedFiles = new[]
        {
            "dim_asset.csv",
            "fact_work_order.csv",
            "bridge_work_order_check.csv",
            "bridge_work_order_part.csv",
            "manifest.json"
        };
        if (!expectedFiles.All(file => File.Exists(Path.Combine(directory, file))) ||
            Directory.EnumerateFiles(directory, "*.html").Any())
        {
            return false;
        }

        var workOrderLines = File.ReadAllLines(Path.Combine(directory, "fact_work_order.csv"));
        var manifest = File.ReadAllText(Path.Combine(directory, "manifest.json"));
        var columns = workOrderLines[0].Split(',');
        var idIndex = Array.IndexOf(columns, "work_order_id");
        var backlogAgeIndex = Array.IndexOf(columns, "backlog_age_hours");
        var rows = workOrderLines.Skip(1)
            .Select(ParseCsvLine)
            .ToDictionary(fields => fields[idIndex], StringComparer.Ordinal);
        return result.WorkOrderCount == dataset.WorkOrders.Count &&
               workOrderLines.Length == dataset.WorkOrders.Count + 1 &&
               idIndex >= 0 &&
               backlogAgeIndex >= 0 &&
               rows["ATUAL"][backlogAgeIndex] == "48" &&
               rows["H1"][backlogAgeIndex] == string.Empty &&
               workOrderLines[0].Contains("evidence_class", StringComparison.Ordinal) &&
               workOrderLines.Skip(1).All(line => line.Contains("synthetic", StringComparison.Ordinal)) &&
               manifest.Contains("fixture.json", StringComparison.Ordinal) &&
               manifest.Contains("Dados sintéticos", StringComparison.Ordinal);
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static string[] ParseCsvLine(string line)
{
    var fields = new List<string>();
    var current = new System.Text.StringBuilder();
    var quoted = false;
    for (var index = 0; index < line.Length; index++)
    {
        var character = line[index];
        if (character == '"')
        {
            if (quoted && index + 1 < line.Length && line[index + 1] == '"')
            {
                current.Append('"');
                index++;
            }
            else
            {
                quoted = !quoted;
            }
        }
        else if (character == ',' && !quoted)
        {
            fields.Add(current.ToString());
            current.Clear();
        }
        else
        {
            current.Append(character);
        }
    }

    if (quoted)
    {
        throw new InvalidDataException("Linha CSV com aspas não balanceadas.");
    }

    fields.Add(current.ToString());
    return fields.ToArray();
}

static bool RejectsFutureBacklog(CmmsDataset dataset)
{
    var exportedAtUtc = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    var futureOrder = dataset.WorkOrders.First(order => !order.IsClosed) with
    {
        OpenedAt = exportedAtUtc.AddMinutes(1)
    };
    var candidate = dataset with
    {
        WorkOrders = dataset.WorkOrders
            .Select(order => order.Id == futureOrder.Id ? futureOrder : order)
            .ToArray()
    };
    var directory = Path.Combine(
        Path.GetTempPath(),
        "copiloto-confiabilidade-bi-future-tests",
        Guid.NewGuid().ToString("N"));

    try
    {
        try
        {
            CmmsBiExporter.ExportAsync(
                    directory,
                    candidate,
                    new CmmsBiExportOptions(
                        "future-test-run",
                        "fixture.json",
                        new string('b', 64),
                        exportedAtUtc))
                .GetAwaiter()
                .GetResult();
            return false;
        }
        catch (InvalidDataException exception)
        {
            return exception.Message.Contains("backlog negativa", StringComparison.Ordinal) &&
                   !Directory.Exists(directory);
        }
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static bool DetectsCsvQualityFindings() => WithAnalysisFixture(
    "dados.csv",
    "tag,valor,valor\nA1,10\nA2,,20\n",
    (engine, path, cache) =>
    {
        var report = engine.AnalyzeAsync(path, cache, "synthetic")
            .GetAwaiter()
            .GetResult();
        return report.EvidenceClass == "derived" &&
               report.SourceEvidenceClass == "synthetic" &&
               report.Findings.Any(finding => finding.RuleId == "CSV_DUPLICATE_HEADER") &&
               report.Findings.Any(finding => finding.RuleId == "CSV_ROW_WIDTH") &&
               report.Findings.All(finding => !string.IsNullOrWhiteSpace(finding.Limitation));
    });

static bool DetectsInvalidJson() => WithAnalysisFixture(
    "dados.json",
    "{\"ativo\":",
    (engine, path, cache) =>
    {
        var report = engine.AnalyzeAsync(path, cache)
            .GetAwaiter()
            .GetResult();
        var finding = report.Findings.Single(item => item.RuleId == "JSON_INVALID");
        return finding.Severity == FindingSeverity.Error &&
               finding.Location.Contains("linha", StringComparison.OrdinalIgnoreCase);
    });

static bool DetectsEmptyJsonObject() => WithAnalysisFixture(
    "vazio.json",
    "{}",
    (engine, path, cache) =>
    {
        var report = engine.AnalyzeAsync(path, cache)
            .GetAwaiter()
            .GetResult();
        return report.Findings.Any(item => item.RuleId == "JSON_EMPTY_ROOT");
    });

static bool DetectsLogMarkersWithLimitation() => WithAnalysisFixture(
    "equipamento.log",
    "INFO inicializado\nWARN temperatura próxima ao limite\nERROR conexão interrompida\n",
    (engine, path, cache) =>
    {
        var report = engine.AnalyzeAsync(path, cache)
            .GetAwaiter()
            .GetResult();
        return report.RecordsInspected == 3 &&
               report.Findings.Count == 2 &&
               report.Findings.All(finding =>
                   finding.Limitation.Contains("não confirma", StringComparison.OrdinalIgnoreCase));
    });

static bool ReusesAnalysisCache() => WithAnalysisFixture(
    "cache.csv",
    "tag,valor\nA1,10\n",
    (engine, path, cache) =>
    {
        var first = engine.AnalyzeAsync(
                path,
                cache,
                "lab_observed",
                new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero))
            .GetAwaiter()
            .GetResult();
        var second = engine.AnalyzeAsync(
                path,
                cache,
                "lab_observed",
                new DateTimeOffset(2026, 11, 7, 13, 0, 0, TimeSpan.Zero))
            .GetAwaiter()
            .GetResult();
        return !first.CacheHit && second.CacheHit &&
               second.SourceSha256 == first.SourceSha256 &&
               second.AnalyzedAtUtc == first.AnalyzedAtUtc &&
               Directory.EnumerateFiles(cache, "*.json").Count() == 1;
    });

static bool WithAnalysisFixture(
    string fileName,
    string content,
    Func<FileAnalysisEngine, string, string, bool> assertion)
{
    var root = Path.Combine(
        Path.GetTempPath(),
        "assistente-patinho-analysis-tests",
        Guid.NewGuid().ToString("N"));
    var cache = Path.Combine(root, "cache");
    Directory.CreateDirectory(root);
    var path = Path.Combine(root, fileName);
    File.WriteAllText(path, content);
    try
    {
        return assertion(new FileAnalysisEngine(), path, cache);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}
