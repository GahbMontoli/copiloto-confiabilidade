using System.Globalization;
using System.Text;

namespace AssistentePatinho.Core;

public sealed record SimilarMaintenanceCase(
    string WorkOrderId,
    string AssetName,
    string Title,
    int Similarity,
    string Cause,
    string Resolution)
{
    public string DisplaySummary => $"{Similarity}% · {WorkOrderId} · {Title}\nCausa: {Cause}\nSolução registrada: {Resolution}";
}

public sealed record MaintenanceAnalysis(
    string Summary,
    int RecurrenceCount,
    IReadOnlyList<SimilarMaintenanceCase> SimilarCases,
    IReadOnlyList<string> RecommendedChecks,
    IReadOnlyList<string> Evidence,
    string Limitations);

public sealed class MaintenanceTriageEngine
{
    public MaintenanceAnalysis Analyze(Asset asset, WorkOrder current, CmmsDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(dataset);

        if (!string.Equals(asset.Id, current.AssetId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"A ordem {current.Id} não pertence ao ativo {asset.Id} selecionado.");
        }

        var closedOrders = dataset.WorkOrders
            .Where(order => order.IsClosed && !string.Equals(order.Id, current.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var recurrenceCount = closedOrders.Count(order =>
            string.Equals(order.AssetId, current.AssetId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(order.FailureCode, current.FailureCode, StringComparison.OrdinalIgnoreCase));

        var similarCases = closedOrders
            .Select(order => new { Order = order, Score = Similarity(asset, current, order, dataset) })
            .Where(item => item.Score >= 20)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Order.ClosedAt)
            .Take(3)
            .Select(item =>
            {
                var caseAsset = dataset.Assets.First(candidate => candidate.Id == item.Order.AssetId);
                return new SimilarMaintenanceCase(
                    item.Order.Id,
                    caseAsset.DisplayName,
                    item.Order.Title,
                    item.Score,
                    item.Order.Cause,
                    item.Order.Resolution);
            })
            .ToArray();

        var selectedIds = similarCases.Select(item => item.WorkOrderId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recommendedChecks = closedOrders
            .Where(order => selectedIds.Contains(order.Id))
            .SelectMany(order => order.Checks)
            .Where(check => !string.IsNullOrWhiteSpace(check))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        if (recommendedChecks.Length == 0)
        {
            recommendedChecks =
            [
                "Confirmar o estado atual do ativo sem alterar parâmetros.",
                "Preservar fotos, alarmes e medições associados à ocorrência.",
                "Validar riscos e bloqueios antes de qualquer intervenção."
            ];
        }

        var evidence = new List<string>
        {
            $"Ativo: {asset.DisplayName} · {asset.Location}",
            $"Falha informada: {current.FailureCode} · prioridade {current.Priority}",
            $"Histórico consultado: {closedOrders.Length} ordens concluídas"
        };
        if (recurrenceCount > 0)
        {
            evidence.Add($"Recorrência no mesmo ativo e código: {recurrenceCount} caso(s)");
        }

        var summary = similarCases.Length == 0
            ? "Não há histórico suficientemente semelhante; a triagem permanece exploratória."
            : $"Foram encontrados {similarCases.Length} caso(s) semelhante(s). O melhor histórico atingiu {similarCases[0].Similarity}% de aderência.";

        return new MaintenanceAnalysis(
            summary,
            recurrenceCount,
            similarCases,
            recommendedChecks,
            evidence,
            "Similaridade histórica apoia a decisão, mas não confirma causa raiz. Toda intervenção exige validação técnica e de segurança.");
    }

    private static int Similarity(Asset currentAsset, WorkOrder current, WorkOrder candidate, CmmsDataset dataset)
    {
        var candidateAsset = dataset.Assets.First(asset => asset.Id == candidate.AssetId);
        var score = 0.0;
        if (string.Equals(current.FailureCode, candidate.FailureCode, StringComparison.OrdinalIgnoreCase)) score += 35;
        if (string.Equals(current.AssetId, candidate.AssetId, StringComparison.OrdinalIgnoreCase)) score += 20;
        if (string.Equals(currentAsset.Category, candidateAsset.Category, StringComparison.OrdinalIgnoreCase)) score += 15;
        if (string.Equals(currentAsset.Model, candidateAsset.Model, StringComparison.OrdinalIgnoreCase)) score += 10;
        score += TokenSimilarity(current.Title + " " + current.Description, candidate.Title + " " + candidate.Description) * 20;
        return Math.Clamp((int)Math.Round(score), 0, 95);
    }

    private static double TokenSimilarity(string first, string second)
    {
        var firstTokens = Tokens(first);
        var secondTokens = Tokens(second);
        if (firstTokens.Count == 0 || secondTokens.Count == 0)
        {
            return 0;
        }

        var intersection = firstTokens.Intersect(secondTokens).Count();
        var union = firstTokens.Union(secondTokens).Count();
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static HashSet<string> Tokens(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            builder.Append(category == UnicodeCategory.NonSpacingMark
                ? ' '
                : char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 2)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
