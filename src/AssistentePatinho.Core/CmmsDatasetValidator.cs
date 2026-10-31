namespace AssistentePatinho.Core;

public static class CmmsDatasetValidator
{
    public static void ValidateOrThrow(CmmsDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        var errors = new List<string>();
        var assets = dataset.Assets ?? [];
        var workOrders = dataset.WorkOrders ?? [];
        if (assets.Count == 0)
        {
            errors.Add("nenhum ativo foi informado");
        }

        if (workOrders.Count == 0)
        {
            errors.Add("nenhuma ordem de serviço foi informada");
        }

        AddDuplicateErrors(assets.Select(asset => asset.Id), "ID de ativo", errors);
        AddDuplicateErrors(assets.Select(asset => asset.Tag), "tag de ativo", errors);
        AddDuplicateErrors(workOrders.Select(order => order.Id), "ID de ordem", errors);

        foreach (var asset in assets)
        {
            if (string.IsNullOrWhiteSpace(asset.Id) || string.IsNullOrWhiteSpace(asset.Tag) || string.IsNullOrWhiteSpace(asset.Name))
            {
                errors.Add("ativo com ID, tag ou nome vazio");
            }
        }

        var assetIds = assets
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Id))
            .Select(asset => asset.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var order in workOrders)
        {
            if (string.IsNullOrWhiteSpace(order.Id) || string.IsNullOrWhiteSpace(order.AssetId) ||
                string.IsNullOrWhiteSpace(order.FailureCode) || string.IsNullOrWhiteSpace(order.Title))
            {
                errors.Add($"ordem {DisplayId(order.Id)} com identificação, ativo, código de falha ou título vazio");
                continue;
            }

            if (!assetIds.Contains(order.AssetId))
            {
                errors.Add($"ordem {order.Id} vinculada ao ativo inexistente {order.AssetId}");
            }

            if (!order.IsOpen && !order.IsClosed)
            {
                errors.Add($"ordem {order.Id} com status não controlado: {DisplayId(order.Status)}");
            }

            if (order.IsClosed && order.ClosedAt is null)
            {
                errors.Add($"ordem concluída {order.Id} sem data de encerramento");
            }

            if (order.IsOpen && order.ClosedAt is not null)
            {
                errors.Add($"ordem aberta {order.Id} com data de encerramento");
            }

            if (order.ClosedAt < order.OpenedAt)
            {
                errors.Add($"ordem {order.Id} encerrada antes da abertura");
            }

            if (order.IsClosed && (string.IsNullOrWhiteSpace(order.Cause) || string.IsNullOrWhiteSpace(order.Resolution)))
            {
                errors.Add($"ordem concluída {order.Id} sem causa ou resolução registrada");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidDataException($"Qualidade dos dados CMMS reprovada: {string.Join("; ", errors.Distinct())}.");
        }
    }

    private static void AddDuplicateErrors(IEnumerable<string> values, string field, ICollection<string> errors)
    {
        foreach (var duplicate in values
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key))
        {
            errors.Add($"{field} duplicado: {duplicate}");
        }
    }

    private static string DisplayId(string? value) => string.IsNullOrWhiteSpace(value) ? "<sem ID>" : value;
}
