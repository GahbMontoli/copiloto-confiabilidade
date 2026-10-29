using AssistentePatinho.Core;

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
Check("inclui evidência rastreável", () => analysis.Evidence.Any(item => item.Contains("ATUAL", StringComparison.OrdinalIgnoreCase) || item.Contains("TEMP_ALTA", StringComparison.OrdinalIgnoreCase)));

var unknown = Order("NOVA", "A3", false, "ELETRICA_DESCONHECIDA", "Ruído novo", "Sintoma ainda não classificado");
var exploratory = new MaintenanceTriageEngine().Analyze(pump, unknown, new CmmsDataset([pump], [unknown]));
Check("mantém fallback sem histórico", () => exploratory.SimilarCases.Count == 0 && exploratory.RecommendedChecks.Count >= 3);

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
        DateTimeOffset.Now.AddDays(-2),
        closed ? DateTimeOffset.Now.AddDays(-1) : null,
        closed ? "Concluída" : "Aberta",
        "Alta",
        failureCode,
        title,
        description,
        closed ? "Causa registrada" : string.Empty,
        closed ? "Solução registrada" : string.Empty,
        checks ?? [],
        []);
