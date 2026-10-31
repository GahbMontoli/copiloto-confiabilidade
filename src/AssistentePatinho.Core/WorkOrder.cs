namespace AssistentePatinho.Core;

public sealed record WorkOrder(
    string Id,
    string AssetId,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    string Status,
    string Priority,
    string FailureCode,
    string Title,
    string Description,
    string Cause,
    string Resolution,
    IReadOnlyList<string> Checks,
    IReadOnlyList<string> Parts)
{
    public bool IsOpen => string.Equals(Status, "Aberta", StringComparison.OrdinalIgnoreCase);
    public bool IsClosed => string.Equals(Status, "Concluída", StringComparison.OrdinalIgnoreCase);
    public string DisplayName => $"{Id} · {Title}";
}

public sealed record CmmsDataset(
    IReadOnlyList<Asset> Assets,
    IReadOnlyList<WorkOrder> WorkOrders);
