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
        if (dataset is null)
        {
            throw new InvalidDataException("O conector CMMS simulado não retornou um conjunto de dados válido.");
        }

        CmmsDatasetValidator.ValidateOrThrow(dataset);
        return dataset;
    }
}
