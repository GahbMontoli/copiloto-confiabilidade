namespace AssistentePatinho.Core;

public sealed record Asset(
    string Id,
    string Tag,
    string Name,
    string Category,
    string Model,
    string Location,
    string Criticality)
{
    public string DisplayName => $"{Tag} · {Name}";
}
