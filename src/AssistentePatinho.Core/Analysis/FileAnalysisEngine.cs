using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssistentePatinho.Core.Analysis;

public sealed class FileAnalysisEngine
{
    public const int CurrentSchemaVersion = 1;
    public const long MaximumSourceBytes = 64L * 1024 * 1024;

    private static readonly HashSet<string> AllowedEvidenceClasses = new(
        ["synthetic", "official_raw", "lab_observed", "field_observed", "derived"],
        StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly IReadOnlyList<IEngineeringFileAnalyzer> _analyzers;

    public FileAnalysisEngine(IEnumerable<IEngineeringFileAnalyzer>? analyzers = null)
    {
        _analyzers = (analyzers ?? BuiltInFileAnalyzers.Create()).ToArray();
        if (_analyzers.Count == 0)
        {
            throw new ArgumentException("At least one file analyzer is required.", nameof(analyzers));
        }
    }

    public async Task<FileAnalysisReport> AnalyzeAsync(
        string path,
        string cacheDirectory,
        string? sourceEvidenceClass = null,
        DateTimeOffset? analyzedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ValidateEvidenceClass(sourceEvidenceClass);

        var fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The source file was not found.", fullPath);
        }

        if (file.Length > MaximumSourceBytes)
        {
            throw new InvalidDataException(
                $"The source file exceeds the {MaximumSourceBytes} byte analysis limit.");
        }

        var extension = file.Extension.ToLowerInvariant();
        var analyzer = _analyzers.FirstOrDefault(candidate =>
            candidate.SupportedExtensions.Contains(extension))
            ?? throw new NotSupportedException($"No local analyzer supports '{extension}'.");
        var sha256 = await ComputeSha256Async(fullPath, cancellationToken).ConfigureAwait(false);
        var resolvedCacheDirectory = Path.GetFullPath(cacheDirectory);
        Directory.CreateDirectory(resolvedCacheDirectory);
        var evidenceKey = sourceEvidenceClass ?? "unspecified";
        var cacheFileName = $"{sha256}-{analyzer.Id}-{analyzer.Version}-{evidenceKey}.json";
        var cachePath = Path.Combine(resolvedCacheDirectory, cacheFileName);

        var cached = await TryReadCacheAsync(
                cachePath,
                sha256,
                analyzer,
                sourceEvidenceClass,
                cancellationToken)
            .ConfigureAwait(false);
        if (cached is not null)
        {
            return cached with { CacheHit = true };
        }

        var result = await analyzer.AnalyzeAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var report = new FileAnalysisReport(
            CurrentSchemaVersion,
            analyzer.Id,
            analyzer.Version,
            file.Name,
            extension,
            sha256,
            file.Length,
            sourceEvidenceClass,
            "derived",
            (analyzedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            result.RecordsInspected,
            false,
            result.Summary,
            result.Findings,
            "Análise local derivada de regras; não confirma causa raiz, conformidade normativa ou condição de campo sem validação técnica.");
        await WriteCacheAtomicallyAsync(cachePath, report, cancellationToken).ConfigureAwait(false);
        return report;
    }

    private static void ValidateEvidenceClass(string? evidenceClass)
    {
        if (evidenceClass is not null && !AllowedEvidenceClasses.Contains(evidenceClass))
        {
            throw new ArgumentException("Unknown source evidence class.", nameof(evidenceClass));
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            useAsync: true);
        return Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
            .ToLowerInvariant();
    }

    private static async Task<FileAnalysisReport?> TryReadCacheAsync(
        string cachePath,
        string expectedSha256,
        IEngineeringFileAnalyzer analyzer,
        string? sourceEvidenceClass,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(cachePath);
            var report = await JsonSerializer.DeserializeAsync<FileAnalysisReport>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            return report is not null &&
                   report.SchemaVersion == CurrentSchemaVersion &&
                   report.SourceSha256 == expectedSha256 &&
                   report.AnalyzerId == analyzer.Id &&
                   report.AnalyzerVersion == analyzer.Version &&
                   report.SourceEvidenceClass == sourceEvidenceClass
                ? report
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task WriteCacheAtomicallyAsync(
        string cachePath,
        FileAnalysisReport report,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16_384,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, report, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, cachePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
