using System.IO;
using System.Windows;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AssistentePatinho.Core;

namespace AssistentePatinho.App;

public partial class MainWindow : Window
{
    private const double ExpandedWidth = 640;
    private const double ExpandedHeight = 760;
    private const double CompactWidth = 205;
    private const double CompactHeight = 215;
    private readonly ICmmsConnector _connector;
    private readonly MaintenanceTriageEngine _triageEngine = new();
    private readonly DispatcherTimer _walkTimer;
    private readonly Random _random = new();
    private CmmsDataset? _dataset;
    private bool _panelOpen;
    private bool _updatingSelectors;
    private double _walkDirection = -1;
    private Point _walkTarget;
    private bool _hasWalkTarget;

    public MainWindow()
    {
        InitializeComponent();
        _connector = new SimulatedCmmsConnector(Path.Combine(AppContext.BaseDirectory, "sample-cmms-data.json"));
        _walkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
        _walkTimer.Tick += WalkTimer_Tick;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Left = SystemParameters.WorkArea.Right - Width - 18;
        Top = SystemParameters.WorkArea.Bottom - Height - 12;
        ((Storyboard)FindResource("DuckIdle")).Begin(this, true);
        DuckSpeechText.Text = "Vou consultar os ativos e ordens do CMMS.";
        SpeechBubble.Visibility = Visibility.Visible;
        SetPanelState(false);
        await LoadCmmsAsync();
    }

    private async Task LoadCmmsAsync()
    {
        try
        {
            _dataset = await _connector.LoadAsync();
            _updatingSelectors = true;
            AssetSelector.ItemsSource = _dataset.Assets;
            AssetSelector.SelectedItem = _dataset.Assets.FirstOrDefault(asset =>
                _dataset.WorkOrders.Any(order => order.AssetId == asset.Id && !order.IsClosed)) ?? _dataset.Assets[0];
            _updatingSelectors = false;
            RefreshOrdersForSelectedAsset();
            ConnectorStatusText.Text = $"CMMS simulado conectado · {_dataset.Assets.Count} ativos · {_dataset.WorkOrders.Count} ordens";
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(34, 160, 107));
            DuckSpeechText.Text = "CMMS carregado. Clique em mim para abrir.";
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            ConnectorStatusText.Text = "Falha ao carregar o CMMS simulado";
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(217, 45, 32));
            DuckSpeechText.Text = "Não consegui carregar os dados de manutenção.";
            CaseSummaryText.Text = ex.Message;
        }
    }

    private void AssetSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingSelectors)
        {
            RefreshOrdersForSelectedAsset();
        }
    }

    private void RefreshOrdersForSelectedAsset(WorkOrder? preferred = null)
    {
        if (_dataset is null || AssetSelector.SelectedItem is not Asset asset)
        {
            return;
        }

        var orders = _dataset.WorkOrders
            .Where(order => order.AssetId == asset.Id)
            .OrderBy(order => order.IsClosed)
            .ThenByDescending(order => order.OpenedAt)
            .ToArray();
        _updatingSelectors = true;
        WorkOrderSelector.ItemsSource = orders;
        WorkOrderSelector.SelectedItem = preferred ?? orders.FirstOrDefault();
        _updatingSelectors = false;
        AnalyzeSelectedCase();
    }

    private void WorkOrderSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingSelectors)
        {
            AnalyzeSelectedCase();
        }
    }

    private void Analyze_Click(object sender, RoutedEventArgs e) => AnalyzeSelectedCase(showReaction: true);

    private void AnalyzeSelectedCase(bool showReaction = false)
    {
        if (_dataset is null || AssetSelector.SelectedItem is not Asset asset || WorkOrderSelector.SelectedItem is not WorkOrder order)
        {
            ClearAnalysis("Selecione um ativo e uma ordem para gerar a triagem.");
            return;
        }

        var analysis = _triageEngine.Analyze(asset, order, _dataset);
        CaseTitleText.Text = order.Title;
        PriorityText.Text = $"{order.Status.ToUpperInvariant()} · {order.Priority.ToUpperInvariant()}";
        AssetDetailsText.Text = $"{asset.DisplayName} · {asset.Model} · {asset.Location} · criticidade {asset.Criticality}";
        CaseSummaryText.Text = analysis.Summary;
        HistoryCountText.Text = _dataset.WorkOrders.Count(item => item.AssetId == asset.Id && item.IsClosed).ToString();
        RecurrenceText.Text = analysis.RecurrenceCount.ToString();
        SimilarCountText.Text = analysis.SimilarCases.Count.ToString();
        EvidenceItems.ItemsSource = analysis.Evidence;
        SimilarCaseItems.ItemsSource = analysis.SimilarCases;
        CheckItems.ItemsSource = analysis.RecommendedChecks;
        LimitationsText.Text = analysis.Limitations;

        if (showReaction)
        {
            DuckSpeechText.Text = analysis.SimilarCases.Count > 0
                ? $"Encontrei {analysis.SimilarCases.Count} casos parecidos no histórico."
                : "Ainda não há histórico suficiente para comparar.";
            SpeechBubble.Visibility = Visibility.Visible;
            ((Storyboard)FindResource("DuckClick")).Begin(this, true);
        }
    }

    private void Simulate_Click(object sender, RoutedEventArgs e)
    {
        if (_dataset is null || AssetSelector.SelectedItem is not Asset asset)
        {
            return;
        }

        var reference = _dataset.WorkOrders
            .Where(order => order.AssetId == asset.Id && order.IsClosed)
            .OrderByDescending(order => order.ClosedAt)
            .FirstOrDefault();
        var simulated = new WorkOrder(
            $"SIM-{DateTime.Now:HHmmss}",
            asset.Id,
            DateTimeOffset.Now,
            null,
            "Aberta",
            asset.Criticality == "Alta" ? "Alta" : "Média",
            reference?.FailureCode ?? "FALHA_NAO_CLASSIFICADA",
            reference is null ? "Nova anomalia para investigação" : $"Nova ocorrência: {reference.Title}",
            reference is null ? "Ocorrência simulada ainda sem diagnóstico." : $"Sintomas semelhantes ao histórico {reference.Id}.",
            string.Empty,
            string.Empty,
            [],
            []);
        _dataset = _dataset with { WorkOrders = _dataset.WorkOrders.Append(simulated).ToArray() };
        RefreshOrdersForSelectedAsset(simulated);
        ConnectorStatusText.Text = $"CMMS simulado · {_dataset.Assets.Count} ativos · {_dataset.WorkOrders.Count} ordens";
        AlertBadge.Visibility = Visibility.Visible;
        StatusDot.Fill = new SolidColorBrush(Color.FromRgb(217, 45, 32));
        DuckSpeechText.Text = "Nova falha simulada. Já busquei ocorrências parecidas.";
        SpeechBubble.Visibility = Visibility.Visible;
        DuckModel.PulseAlert();
        ((Storyboard)FindResource("DuckAlert")).Begin(this, true);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ClearAnalysis("Selecione uma ordem para começar.");
        WorkOrderSelector.SelectedIndex = -1;
        AlertBadge.Visibility = Visibility.Collapsed;
        StatusDot.Fill = new SolidColorBrush(Color.FromRgb(34, 160, 107));
        DuckSpeechText.Text = "Painel limpo. Os dados simulados continuam disponíveis.";
    }

    private void ClearAnalysis(string message)
    {
        CaseTitleText.Text = "Nenhuma ordem selecionada";
        PriorityText.Text = string.Empty;
        AssetDetailsText.Text = string.Empty;
        CaseSummaryText.Text = message;
        HistoryCountText.Text = "0";
        RecurrenceText.Text = "0";
        SimilarCountText.Text = "0";
        EvidenceItems.ItemsSource = null;
        SimilarCaseItems.ItemsSource = null;
        CheckItems.ItemsSource = null;
        LimitationsText.Text = string.Empty;
    }

    private void DuckButton_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        TogglePanel();
        AlertBadge.Visibility = Visibility.Collapsed;
        StatusDot.Fill = new SolidColorBrush(Color.FromRgb(34, 160, 107));
        DuckSpeechText.Text = _panelOpen ? "Vamos revisar ativos e ordens." : "Continuo acompanhando o CMMS.";
        SpeechBubble.Visibility = Visibility.Visible;
        ((Storyboard)FindResource("DuckClick")).Begin(this, true);
    }

    private void DuckHead_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1 && e.LeftButton == MouseButtonState.Pressed)
        {
            StopWalking();
            DragMove();
            ClampWindowToWorkArea();
            _hasWalkTarget = false;
            if (!_panelOpen)
            {
                StartWalking();
            }
            e.Handled = true;
        }
    }

    private void DuckButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e) => TogglePanel();
    private void TogglePanel() => SetPanelState(!_panelOpen);
    private void ClosePanel_Click(object sender, RoutedEventArgs e) => SetPanelState(false);

    private void SetPanelState(bool open)
    {
        var anchorRight = Left + Width;
        var anchorBottom = Top + Height;
        _panelOpen = open;
        PanelCard.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        Width = open ? ExpandedWidth : CompactWidth;
        Height = open ? ExpandedHeight : CompactHeight;
        Left = anchorRight - Width;
        Top = anchorBottom - Height;
        ClampWindowToWorkArea();
        if (open) StopWalking(); else StartWalking();
    }

    private void StartWalking()
    {
        if (_panelOpen) return;
        DuckModel.SetMode(DuckAnimationMode.Walking);
        if (!_hasWalkTarget) ChooseNextWalkTarget();
        DuckFacingTransform.ScaleX = _walkDirection;
        _walkTimer.Start();
    }

    private void StopWalking()
    {
        _walkTimer.Stop();
        DuckModel.SetMode(DuckAnimationMode.Idle);
    }

    private void WalkTimer_Tick(object? sender, EventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        var deltaX = _walkTarget.X - Left;
        var deltaY = _walkTarget.Y - Top;
        var distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (distance < 8)
        {
            ChooseNextWalkTarget();
            return;
        }

        const double pixelsPerTick = 2.1;
        Left = Math.Clamp(Left + (deltaX / distance * pixelsPerTick), workArea.Left, workArea.Right - Width);
        Top = Math.Clamp(Top + (deltaY / distance * pixelsPerTick), workArea.Top, workArea.Bottom - Height);
        _walkDirection = deltaX < 0 ? -1 : 1;
        DuckFacingTransform.ScaleX = _walkDirection;
    }

    private void ChooseNextWalkTarget()
    {
        var workArea = SystemParameters.WorkArea;
        var maxLeft = Math.Max(workArea.Left, workArea.Right - Width);
        var maxTop = Math.Max(workArea.Top, workArea.Bottom - Height);
        _walkTarget = new Point(
            workArea.Left + (_random.NextDouble() * (maxLeft - workArea.Left)),
            workArea.Top + (_random.NextDouble() * (maxTop - workArea.Top)));
        _walkDirection = _walkTarget.X < Left ? -1 : 1;
        _hasWalkTarget = true;
    }

    private void ClampWindowToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        Left = Math.Clamp(Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
        Top = Math.Clamp(Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        _walkTimer.Stop();
    }
}
