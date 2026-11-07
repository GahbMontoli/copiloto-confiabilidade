using System.IO;
using System.Windows;
using System.Windows.Input;
using AssistentePatinho.Core.Analysis;
using Microsoft.Win32;

namespace AssistentePatinho.App;

public partial class FileAnalysisWindow : Window
{
    private readonly FileAnalysisEngine _analysisEngine = new();
    private CancellationTokenSource? _analysisCancellation;
    private string? _sourcePath;

    public FileAnalysisWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    private void SelectFile_Click(object sender, RoutedEventArgs e) => SelectSourceFile();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && key == Key.S)
        {
            e.Handled = true;
            SelectSourceFile();
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && key == Key.E && RunAnalysisButton.IsEnabled)
        {
            e.Handled = true;
            _ = RunAnalysisAsync();
        }
    }

    private void SelectSourceFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar arquivo para análise local",
            Filter = "Arquivos suportados (*.csv;*.json;*.log;*.txt)|*.csv;*.json;*.log;*.txt|CSV (*.csv)|*.csv|JSON (*.json)|*.json|Logs e textos (*.log;*.txt)|*.log;*.txt",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        FilePathTextBox.Text = dialog.FileName;
    }

    private void FilePathTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (RunAnalysisButton is null)
        {
            return;
        }

        var candidate = FilePathTextBox.Text.Trim();
        _sourcePath = File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        RunAnalysisButton.IsEnabled = _sourcePath is not null;
        AnalysisStatusText.Text = _sourcePath is null
            ? "Informe o caminho de um arquivo local existente."
            : "Arquivo pronto para análise.";
        CacheStatusText.Text = _sourcePath is null ? "AGUARDANDO" : "PRONTO";
    }

    private void FilePathTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && RunAnalysisButton.IsEnabled)
        {
            e.Handled = true;
            _ = RunAnalysisAsync();
        }
    }

    private async void RunAnalysis_Click(object sender, RoutedEventArgs e) => await RunAnalysisAsync();

    private async Task RunAnalysisAsync()
    {
        if (string.IsNullOrWhiteSpace(_sourcePath))
        {
            return;
        }

        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
        _analysisCancellation = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            var cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AssistentePatinho",
                "analysis-cache");
            var report = await _analysisEngine.AnalyzeAsync(
                _sourcePath,
                cacheDirectory,
                SelectedEvidenceClass(),
                cancellationToken: _analysisCancellation.Token);
            ShowReport(report);
        }
        catch (OperationCanceledException)
        {
            AnalysisStatusText.Text = "Análise cancelada.";
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidDataException or
            NotSupportedException or ArgumentException)
        {
            ShowFailure(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private string? SelectedEvidenceClass() => EvidenceClassSelector.SelectedIndex switch
    {
        1 => "synthetic",
        2 => "official_raw",
        3 => "lab_observed",
        4 => "field_observed",
        5 => "derived",
        _ => null
    };

    private void ShowReport(FileAnalysisReport report)
    {
        var findings = report.Findings.Select(finding => new FindingView(
            TranslateSeverity(finding.Severity),
            $"{finding.RuleId} v{finding.RuleVersion}",
            finding.Location,
            finding.Message,
            $"Evidência: {finding.Evidence}",
            $"Verificar: {finding.RecommendedVerification}",
            $"Limitação: {finding.Limitation}"))
            .ToArray();
        FindingItems.ItemsSource = findings;
        EmptyFindingsPanel.Visibility = findings.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyFindingsText.Text = findings.Length == 0
            ? "Nenhuma inconsistência foi encontrada pelas regras atuais."
            : string.Empty;
        FindingCountText.Text = findings.Length == 1 ? "1 achado" : $"{findings.Length} achados";
        SummaryText.Text = report.Summary;
        SourceMetadataText.Text =
            $"{report.SourceFileName} · {report.SourceBytes:N0} bytes · {report.RecordsInspected:N0} registros inspecionados · fonte {report.SourceEvidenceClass ?? "não declarada"} · resultado {report.EvidenceClass}";
        ChecksumText.Text = $"SHA-256: {report.SourceSha256}";
        InterpretationLimitText.Text = report.InterpretationLimit;
        CacheStatusText.Text = report.CacheHit ? "CACHE REUTILIZADO" : "ANÁLISE EXECUTADA";
        AnalysisStatusText.Text = findings.Length == 0
            ? "Análise concluída sem achados pelas regras atuais."
            : "Análise concluída. Revise cada achado antes de tomar decisões.";
    }

    private void ShowFailure(string message)
    {
        FindingItems.ItemsSource = null;
        EmptyFindingsPanel.Visibility = Visibility.Visible;
        EmptyFindingsText.Text = "Não foi possível analisar o arquivo selecionado.";
        FindingCountText.Text = "falha controlada";
        SummaryText.Text = message;
        SourceMetadataText.Text = string.Empty;
        ChecksumText.Text = string.Empty;
        CacheStatusText.Text = "FALHA";
        AnalysisStatusText.Text = "Corrija a entrada e tente novamente.";
        InterpretationLimitText.Text = "Nenhuma conclusão técnica foi produzida nesta tentativa.";
    }

    private void SetBusy(bool busy)
    {
        RunAnalysisButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(_sourcePath);
        EvidenceClassSelector.IsEnabled = !busy;
        AnalysisStatusText.Text = busy ? "Analisando localmente..." : AnalysisStatusText.Text;
        if (busy)
        {
            CacheStatusText.Text = "PROCESSANDO";
        }
    }

    private static string TranslateSeverity(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Error => "ERRO",
        FindingSeverity.Warning => "AVISO",
        _ => "INFORMAÇÃO"
    };

    private void OnClosed(object? sender, EventArgs e)
    {
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
    }

    private sealed record FindingView(
        string Severity,
        string Rule,
        string Location,
        string Message,
        string Evidence,
        string Verification,
        string Limitation);
}
