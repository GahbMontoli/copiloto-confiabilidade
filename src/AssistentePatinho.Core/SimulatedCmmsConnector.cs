using System.Text.Json;

namespace AssistentePatinho.Core;

public interface ICmmsConnector
{
    Task<CmmsDataset> LoadAsync(CancellationToken cancellationToken = default);
}

public sealed class SimulatedCmmsConnector(string dataPath) : ICmmsConnector
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<CmmsDataset> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(dataPath);
        var dataset = await JsonSerializer.DeserializeAsync<CmmsDataset>(stream, Options, cancellationToken);
        if (dataset is null || dataset.Assets.Count == 0 || dataset.WorkOrders.Count == 0)
        {
            throw new InvalidDataException("O conector CMMS simulado não retornou ativos e ordens válidos.");
        }

        var assetIds = dataset.Assets.Select(asset => asset.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (dataset.WorkOrders.Any(order => !assetIds.Contains(order.AssetId)))
        {
            throw new InvalidDataException("Existem ordens vinculadas a ativos inexistentes.");
        }

        return dataset;
    }
}
