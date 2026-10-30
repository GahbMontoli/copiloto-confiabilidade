using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AssistentePatinho.Core;

public sealed record CmmsBiExportOptions(
    string RunId,
    string SourceId,
    string SourceSha256,
    DateTimeOffset ExportedAtUtc,
    string EvidenceClass = "synthetic");

public sealed record CmmsBiExportResult(
    string OutputDirectory,
    int AssetCount,
    int WorkOrderCount,
    int CheckCount,
    int PartCount);

public static class CmmsBiExporter
{
    public const int CurrentSchemaVersion = 1;
    public const string ProjectId = "copiloto-confiabilidade";

    public static string ComputeSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    public static async Task<CmmsBiExportResult> ExportAsync(
        string outputDirectory,
        CmmsDataset dataset,
        CmmsBiExportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        CmmsDatasetValidator.ValidateOrThrow(dataset);

        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var exportedAtUtc = options.ExportedAtUtc.ToUniversalTime();
        var checkCount = dataset.WorkOrders.Sum(order => order.Checks.Count);
        var partCount = dataset.WorkOrders.Sum(order => order.Parts.Count);

        await WriteAssetsAsync(
            Path.Combine(outputDirectory, "dim_asset.csv"),
            dataset.Assets,
            options,
            cancellationToken);
        await WriteWorkOrdersAsync(
            Path.Combine(outputDirectory, "fact_work_order.csv"),
            dataset.WorkOrders,
            options,
            exportedAtUtc,
            cancellationToken);
        await WriteListBridgeAsync(
            Path.Combine(outputDirectory, "bridge_work_order_check.csv"),
            "check_text",
            dataset.WorkOrders.SelectMany(order =>
                order.Checks.Select((value, index) => (order, index, value))),
            options,
            exportedAtUtc,
            cancellationToken);
        await WriteListBridgeAsync(
            Path.Combine(outputDirectory, "bridge_work_order_part.csv"),
            "part_text",
            dataset.WorkOrders.SelectMany(order =>
                order.Parts.Select((value, index) => (order, index, value))),
            options,
            exportedAtUtc,
            cancellationToken);

        var manifest = new
        {
            schemaVersion = CurrentSchemaVersion,
            projectId = ProjectId,
            runId = options.RunId,
            sourceId = options.SourceId,
            sourceSha256 = options.SourceSha256,
            exportedAtUtc,
            evidenceClass = options.EvidenceClass,
            qualityStatus = "valid",
            evidenceLimit =
                "Dados sintéticos validam o fluxo do software e não representam histórico de manutenção real.",
            tables = new
            {
                dimAsset = dataset.Assets.Count,
                factWorkOrder = dataset.WorkOrders.Count,
                bridgeWorkOrderCheck = checkCount,
                bridgeWorkOrderPart = partCount
            }
        };
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "manifest.json"),
            JsonSerializer.Serialize(
                manifest,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }),
            new UTF8Encoding(false),
            cancellationToken);

        return new CmmsBiExportResult(
            outputDirectory,
            dataset.Assets.Count,
            dataset.WorkOrders.Count,
            checkCount,
            partCount);
    }

    private static async Task WriteAssetsAsync(
        string path,
        IReadOnlyList<Asset> assets,
        CmmsBiExportOptions options,
        CancellationToken cancellationToken)
    {
        await using var writer = CreateWriter(path);
        await writer.WriteLineAsync(
            "schema_version,project_id,run_id,source_id,asset_id,tag,asset_name," +
            "category,model,location,criticality,evidence_class,quality_status");
        foreach (var asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(Join(
                CurrentSchemaVersion,
                ProjectId,
                options.RunId,
                options.SourceId,
                asset.Id,
                asset.Tag,
                asset.Name,
                asset.Category,
                asset.Model,
                asset.Location,
                asset.Criticality,
                options.EvidenceClass,
                "valid"));
        }
    }

    private static async Task WriteWorkOrdersAsync(
        string path,
        IReadOnlyList<WorkOrder> workOrders,
        CmmsBiExportOptions options,
        DateTimeOffset exportedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var writer = CreateWriter(path);
        await writer.WriteLineAsync(
            "schema_version,project_id,run_id,source_id,asset_id,observed_at_utc," +
            "ingested_at_utc,evidence_class,quality_status,work_order_id,opened_at_utc," +
            "closed_at_utc,is_closed,duration_hours,status,priority,failure_code,title," +
            "has_cause,has_resolution,checks_count,parts_count");
        foreach (var order in workOrders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var openedAtUtc = order.OpenedAt.ToUniversalTime();
            var closedAtUtc = order.ClosedAt?.ToUniversalTime();
            var duration = closedAtUtc is null
                ? string.Empty
                : (closedAtUtc.Value - openedAtUtc).TotalHours.ToString("0.###", CultureInfo.InvariantCulture);
            await writer.WriteLineAsync(Join(
                CurrentSchemaVersion,
                ProjectId,
                options.RunId,
                options.SourceId,
                order.AssetId,
                openedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                exportedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                options.EvidenceClass,
                "valid",
                order.Id,
                openedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                closedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                Boolean(order.IsClosed),
                duration,
                order.Status,
                order.Priority,
                order.FailureCode,
                order.Title,
                Boolean(!string.IsNullOrWhiteSpace(order.Cause)),
                Boolean(!string.IsNullOrWhiteSpace(order.Resolution)),
                order.Checks.Count,
                order.Parts.Count));
        }
    }

    private static async Task WriteListBridgeAsync(
        string path,
        string valueColumn,
        IEnumerable<(WorkOrder Order, int Index, string Value)> values,
        CmmsBiExportOptions options,
        DateTimeOffset exportedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var writer = CreateWriter(path);
        await writer.WriteLineAsync(
            $"schema_version,project_id,run_id,source_id,work_order_id,ordinal,{valueColumn}," +
            "ingested_at_utc,evidence_class,quality_status");
        foreach (var item in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(Join(
                CurrentSchemaVersion,
                ProjectId,
                options.RunId,
                options.SourceId,
                item.Order.Id,
                item.Index + 1,
                item.Value,
                exportedAtUtc.ToString("O", CultureInfo.InvariantCulture),
                options.EvidenceClass,
                "valid"));
        }
    }

    private static StreamWriter CreateWriter(string path) => new(
        File.Create(path),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private static string Join(params object[] values) =>
        string.Join(',', values.Select(value => Escape(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)));

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    private static string Boolean(bool value) => value ? "true" : "false";

    private static void ValidateOptions(CmmsBiExportOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SourceSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.EvidenceClass);
        if (options.SourceSha256.Length != 64 || !options.SourceSha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("O SHA-256 da fonte deve conter 64 caracteres hexadecimais.", nameof(options));
        }
        if (!string.Equals(options.EvidenceClass, "synthetic", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "O exportador demonstrativo aceita somente evidence_class=synthetic.",
                nameof(options));
        }
    }
}
