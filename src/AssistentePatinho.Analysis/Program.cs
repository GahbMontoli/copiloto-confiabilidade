using System.Text.Json;
using AssistentePatinho.Core.Analysis;

if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("Uso: AssistentePatinho.Analysis <arquivo> [--cache <diretorio>] [--evidence-class <classe>]");
    Console.WriteLine("Classes: synthetic, official_raw, lab_observed, field_observed, derived.");
    return args.Length == 0 ? 1 : 0;
}

try
{
    var sourcePath = args[0];
    var cacheDirectory = GetOption(args, "--cache") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AssistentePatinho",
        "analysis-cache");
    var evidenceClass = GetOption(args, "--evidence-class");
    var report = await new FileAnalysisEngine().AnalyzeAsync(
        sourcePath,
        cacheDirectory,
        evidenceClass);
    Console.WriteLine(JsonSerializer.Serialize(report, FileAnalysisEngine.CreateJsonOptions()));
    return report.Findings.Any(finding => finding.Severity == FindingSeverity.Error) ? 2 : 0;
}
catch (Exception exception) when (exception is
    IOException or UnauthorizedAccessException or InvalidDataException or
    NotSupportedException or ArgumentException)
{
    Console.Error.WriteLine($"Falha na análise local: {exception.Message}");
    return 1;
}

static string? GetOption(string[] arguments, string name)
{
    var index = Array.FindIndex(arguments, argument =>
        string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
    if (index < 0)
    {
        return null;
    }

    if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
    {
        throw new ArgumentException($"A opção {name} exige um valor.");
    }

    return arguments[index + 1];
}
