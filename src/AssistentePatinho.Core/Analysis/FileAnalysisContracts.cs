namespace AssistentePatinho.Core.Analysis;

public enum FindingSeverity
{
    Information,
    Warning,
    Error
}

public sealed record AnalysisFinding(
    string RuleId,
    string RuleVersion,
    FindingSeverity Severity,
    double Confidence,
    string Location,
    string Message,
    string Evidence,
    string RecommendedVerification,
    string Limitation);

public sealed record FileAnalysisResult(
    string Summary,
    int RecordsInspected,
    IReadOnlyList<AnalysisFinding> Findings);

public sealed record FileAnalysisReport(
    int SchemaVersion,
    string AnalyzerId,
    string AnalyzerVersion,
    string SourceFileName,
    string SourceExtension,
    string SourceSha256,
    long SourceBytes,
    string? SourceEvidenceClass,
    string EvidenceClass,
    DateTimeOffset AnalyzedAtUtc,
    int RecordsInspected,
    bool CacheHit,
    string Summary,
    IReadOnlyList<AnalysisFinding> Findings,
    string InterpretationLimit);

public interface IEngineeringFileAnalyzer
{
    string Id { get; }
    string Version { get; }
    IReadOnlySet<string> SupportedExtensions { get; }

    Task<FileAnalysisResult> AnalyzeAsync(
        string path,
        CancellationToken cancellationToken = default);
}
