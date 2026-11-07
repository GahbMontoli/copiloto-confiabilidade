using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AssistentePatinho.Core.Analysis;

public static class BuiltInFileAnalyzers
{
    public static IReadOnlyList<IEngineeringFileAnalyzer> Create() =>
        [new CsvQualityAnalyzer(), new JsonQualityAnalyzer(), new LogQualityAnalyzer()];
}

internal sealed class CsvQualityAnalyzer : IEngineeringFileAnalyzer
{
    public string Id => "csv-quality";
    public string Version => "1.0.0";
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>([".csv"]);

    public async Task<FileAnalysisResult> AnalyzeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var findings = new List<AnalysisFinding>();
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var headerLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (headerLine is null)
        {
            findings.Add(Finding("CSV_EMPTY_FILE", FindingSeverity.Error, "arquivo", "O CSV está vazio.",
                "Nenhum cabeçalho foi encontrado.", "Confirmar a origem e exportar novamente."));
            return new FileAnalysisResult("CSV vazio; nenhuma linha analisada.", 0, findings);
        }

        var delimiter = DetectDelimiter(headerLine);
        var headers = ParseLine(headerLine, delimiter, out var balancedHeader);
        if (!balancedHeader)
        {
            findings.Add(Finding("CSV_UNBALANCED_QUOTES", FindingSeverity.Error, "linha 1",
                "O cabeçalho contém aspas não balanceadas.", Truncate(headerLine),
                "Corrigir a codificação CSV na origem."));
        }

        var emptyHeaders = headers
            .Select((value, index) => new { value, index })
            .Where(item => string.IsNullOrWhiteSpace(item.value))
            .Select(item => item.index + 1)
            .ToArray();
        if (emptyHeaders.Length > 0)
        {
            findings.Add(Finding("CSV_EMPTY_HEADER", FindingSeverity.Error, "linha 1",
                "Há colunas sem nome.", $"Posições: {string.Join(", ", emptyHeaders)}",
                "Nomear todas as colunas antes da análise."));
        }

        var duplicates = headers
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            findings.Add(Finding("CSV_DUPLICATE_HEADER", FindingSeverity.Error, "linha 1",
                "O CSV possui nomes de coluna duplicados.", string.Join(", ", duplicates),
                "Renomear as colunas para preservar um significado único."));
        }

        var missingByColumn = new int[headers.Count];
        var records = 0;
        var lineNumber = 1;
        var widthErrors = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            var fields = ParseLine(line, delimiter, out var balanced);
            if (!balanced || fields.Count != headers.Count)
            {
                widthErrors++;
                if (widthErrors <= 10)
                {
                    findings.Add(Finding("CSV_ROW_WIDTH", FindingSeverity.Error, $"linha {lineNumber}",
                        "A quantidade de campos diverge do cabeçalho ou há aspas não balanceadas.",
                        $"Esperado: {headers.Count}; encontrado: {fields.Count}.",
                        "Revisar delimitadores, aspas e quebras de linha do registro."));
                }
            }

            for (var index = 0; index < Math.Min(fields.Count, missingByColumn.Length); index++)
            {
                if (string.IsNullOrWhiteSpace(fields[index]))
                {
                    missingByColumn[index]++;
                }
            }

            records++;
        }

        if (widthErrors > 10)
        {
            findings.Add(Finding("CSV_ROW_WIDTH_SUMMARY", FindingSeverity.Warning, "arquivo",
                "Outras linhas com estrutura divergente foram resumidas.",
                $"Total de linhas divergentes: {widthErrors}.",
                "Corrigir a exportação antes de consumir o conjunto."));
        }

        if (records == 0)
        {
            findings.Add(Finding("CSV_NO_DATA", FindingSeverity.Warning, "arquivo",
                "O CSV contém cabeçalho, mas nenhuma linha de dados.",
                $"Colunas detectadas: {headers.Count}.", "Confirmar se o período consultado possui registros."));
        }
        else
        {
            for (var index = 0; index < missingByColumn.Length; index++)
            {
                var ratio = (double)missingByColumn[index] / records;
                if (ratio >= 0.2)
                {
                    findings.Add(Finding("CSV_MISSING_VALUES", FindingSeverity.Warning,
                        $"coluna {headers[index]}", "A coluna possui pelo menos 20% de valores ausentes.",
                        $"Ausentes: {missingByColumn[index]}/{records} ({ratio:P1}).",
                        "Verificar a regra de preenchimento; não substituir ausências por zero sem justificativa."));
                }
            }
        }

        return new FileAnalysisResult(
            $"CSV analisado localmente: {records} registros, {headers.Count} colunas e {findings.Count} achados.",
            records,
            findings);
    }

    private static char DetectDelimiter(string header)
    {
        var candidates = new[] { ',', ';', '\t' };
        return candidates
            .OrderByDescending(candidate => CountOutsideQuotes(header, candidate))
            .First();
    }

    private static int CountOutsideQuotes(string value, char delimiter)
    {
        var quoted = false;
        var count = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '"')
            {
                if (quoted && index + 1 < value.Length && value[index + 1] == '"')
                {
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (!quoted && value[index] == delimiter)
            {
                count++;
            }
        }

        return count;
    }

    private static IReadOnlyList<string> ParseLine(string line, char delimiter, out bool balanced)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == delimiter && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        fields.Add(current.ToString());
        balanced = !quoted;
        return fields;
    }

    private static AnalysisFinding Finding(
        string id,
        FindingSeverity severity,
        string location,
        string message,
        string evidence,
        string verification) => new(
        id, "1.0", severity, 1.0, location, message, evidence, verification,
        "Regra estrutural; não avalia o significado de engenharia nem a causa do problema.");

    private static string Truncate(string value) =>
        value.Length <= 240 ? value : value[..240] + "…";
}

internal sealed class JsonQualityAnalyzer : IEngineeringFileAnalyzer
{
    public string Id => "json-quality";
    public string Version => "1.0.0";
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>([".json"]);

    public async Task<FileAnalysisResult> AnalyzeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var findings = new List<AnalysisFinding>();
        try
        {
            await using var stream = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(
                    stream,
                    new JsonDocumentOptions { AllowTrailingCommas = false, MaxDepth = 128 },
                    cancellationToken)
                .ConfigureAwait(false);
            var statistics = Count(document.RootElement);
            var records = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.GetArrayLength()
                : 1;
            var emptyRoot = document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.GetArrayLength() == 0,
                JsonValueKind.Object => !document.RootElement.EnumerateObject().Any(),
                _ => false
            };
            if (emptyRoot)
            {
                findings.Add(new AnalysisFinding(
                    "JSON_EMPTY_ROOT", "1.0", FindingSeverity.Warning, 1.0, "$",
                    "A estrutura JSON principal está vazia.", "Nenhum item foi encontrado.",
                    "Confirmar se a consulta ou exportação deveria produzir registros.",
                    "Regra estrutural; não avalia o significado de engenharia dos campos."));
            }

            if (statistics.Nodes >= 10 && (double)statistics.Nulls / statistics.Nodes >= 0.2)
            {
                findings.Add(new AnalysisFinding(
                    "JSON_NULL_RATIO", "1.0", FindingSeverity.Warning, 1.0, "$",
                    "Pelo menos 20% dos valores JSON são nulos.",
                    $"Nulos: {statistics.Nulls}/{statistics.Nodes} ({(double)statistics.Nulls / statistics.Nodes:P1}).",
                    "Verificar a origem e documentar a semântica dos valores ausentes.",
                    "A contagem inclui nós estruturais e não mede completude de negócio por campo."));
            }

            return new FileAnalysisResult(
                $"JSON válido analisado localmente: {statistics.Nodes} nós, {statistics.Nulls} nulos e {findings.Count} achados.",
                records,
                findings);
        }
        catch (JsonException exception)
        {
            findings.Add(new AnalysisFinding(
                "JSON_INVALID", "1.0", FindingSeverity.Error, 1.0,
                $"linha {(exception.LineNumber ?? 0) + 1}, byte {(exception.BytePositionInLine ?? 0) + 1}",
                "O arquivo não contém JSON válido.", exception.Message,
                "Corrigir a sintaxe ou gerar novamente o arquivo na origem.",
                "A análise foi interrompida antes de interpretar qualquer conteúdo."));
            return new FileAnalysisResult("JSON inválido; análise estrutural interrompida.", 0, findings);
        }
    }

    private static (int Nodes, int Nulls) Count(JsonElement element)
    {
        var nodes = 1;
        var nulls = element.ValueKind == JsonValueKind.Null ? 1 : 0;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var child = Count(property.Value);
                nodes += child.Nodes;
                nulls += child.Nulls;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var child = Count(item);
                nodes += child.Nodes;
                nulls += child.Nulls;
            }
        }

        return (nodes, nulls);
    }
}

internal sealed class LogQualityAnalyzer : IEngineeringFileAnalyzer
{
    public string Id => "log-quality";
    public string Version => "1.0.0";
    public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>([".log", ".txt"]);

    public async Task<FileAnalysisResult> AnalyzeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var counts = new Dictionary<string, (int Count, int FirstLine, string Evidence)>(
            StringComparer.OrdinalIgnoreCase);
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lineNumber = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            foreach (var token in new[] { "FATAL", "ERROR", "EXCEPTION", "WARN" })
            {
                if (!line.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (counts.TryGetValue(token, out var current))
                {
                    counts[token] = (current.Count + 1, current.FirstLine, current.Evidence);
                }
                else
                {
                    counts[token] = (1, lineNumber, Truncate(line));
                }
            }
        }

        var findings = counts.Select(entry => new AnalysisFinding(
                $"LOG_{entry.Key.ToUpperInvariant()}_TOKEN",
                "1.0",
                entry.Key.Equals("WARN", StringComparison.OrdinalIgnoreCase)
                    ? FindingSeverity.Warning
                    : FindingSeverity.Error,
                0.8,
                $"primeira ocorrência na linha {entry.Value.FirstLine}",
                $"O marcador textual {entry.Key} apareceu {entry.Value.Count} vez(es).",
                entry.Value.Evidence,
                "Confirmar o formato do log e correlacionar a ocorrência com timestamp, ativo e estado operacional.",
                "Detecção lexical; a presença do termo não confirma falha, severidade ou causa raiz."))
            .OrderByDescending(finding => finding.Severity)
            .ToArray();
        return new FileAnalysisResult(
            $"Log analisado localmente: {lineNumber} linhas e {findings.Length} classes de marcador encontradas.",
            lineNumber,
            findings);
    }

    private static string Truncate(string value) =>
        value.Length <= 240 ? value : value[..240] + "…";
}
