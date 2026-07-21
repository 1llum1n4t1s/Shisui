using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services;
using Shisui.UI.Services;

namespace Shisui.UI.ViewModels;

/// <summary>安全なクイック最適化と、6項目の実測A/B最適化を一か所に集約する。</summary>
public partial class AutoOptimizationViewModel : ObservableObject
{
    private const int BenchmarkLoadSizeBytes = 5_000_000;
    private const double PingSignificanceMs = 1.0;
    private const double SpeedSignificanceRatio = 0.03;
    private readonly INetworkMutationGate _networkMutationGate;
    private readonly IAutoTuningBenchmarkService? _autoTuningBenchmarkService;
    private readonly IRscBenchmarkService? _rscBenchmarkService;
    private readonly IBbr2BenchmarkService? _bbr2BenchmarkService;
    private readonly ITcpOptionBenchmarkService? _tcpOptionBenchmarkService;
    private readonly ITcpTuningService? _tcpTuningService;
    private readonly ILegacyNetworkDiagnosticsService? _legacyNetworkDiagnosticsService;
    private readonly TcpTuningViewModel? _tcpTuningViewModel;
    private CancellationTokenSource? _benchmarkCts;

    public AutoOptimizationViewModel(
        DnsSettingsViewModel dnsSettings,
        INetworkMutationGate networkMutationGate,
        IAutoTuningBenchmarkService? autoTuningBenchmarkService = null,
        IRscBenchmarkService? rscBenchmarkService = null,
        IBbr2BenchmarkService? bbr2BenchmarkService = null,
        ITcpOptionBenchmarkService? tcpOptionBenchmarkService = null,
        ITcpTuningService? tcpTuningService = null,
        ILegacyNetworkDiagnosticsService? legacyNetworkDiagnosticsService = null,
        TcpTuningViewModel? tcpTuningViewModel = null)
    {
        DnsSettings = dnsSettings;
        _networkMutationGate = networkMutationGate;
        _autoTuningBenchmarkService = autoTuningBenchmarkService;
        _rscBenchmarkService = rscBenchmarkService;
        _bbr2BenchmarkService = bbr2BenchmarkService;
        _tcpOptionBenchmarkService = tcpOptionBenchmarkService;
        _tcpTuningService = tcpTuningService;
        _legacyNetworkDiagnosticsService = legacyNetworkDiagnosticsService;
        _tcpTuningViewModel = tcpTuningViewModel;

        BinaryGroups =
        [
            new("BBR2 — ダウンロード負荷時Ping"),
            new("RSC — ダウンロード負荷時Ping"),
            new("ECN — ダウンロード負荷時Ping"),
            new("RSS — ダウンロード速度"),
            new("TCPタイムスタンプ — ダウンロード負荷時Ping"),
        ];

        DnsSettings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DnsSettingsViewModel.IsBusy))
            {
                OnPropertyChanged(nameof(IsOperationRunning));
                OnPropertyChanged(nameof(CanApplyRecommendations));
            }
            else if (e.PropertyName == nameof(DnsSettingsViewModel.SelectedAdapter))
            {
                LegacyNetworkFindings.Clear();
                LegacyDiagnosticsStatusText = string.Empty;
                HasLegacyDiagnosticsWarnings = false;
                CanResetSelectedNic = false;
            }
        };
    }

    public event EventHandler<CommandExecutionResult>? CommandExecuted;
    public DnsSettingsViewModel DnsSettings { get; }
    public ObservableCollection<AutoTuningBenchmarkRow> AutoTuningResults { get; } = [];
    public ObservableCollection<BinaryOptimizationGroup> BinaryGroups { get; }
    public ObservableCollection<LegacyNetworkDiagnosticFinding> LegacyNetworkFindings { get; } = [];
    private BinaryOptimizationGroup Bbr2Group => BinaryGroups[0];
    private BinaryOptimizationGroup RscGroup => BinaryGroups[1];
    private BinaryOptimizationGroup EcnGroup => BinaryGroups[2];
    private BinaryOptimizationGroup RssGroup => BinaryGroups[3];
    private BinaryOptimizationGroup TimestampsGroup => BinaryGroups[4];

    public bool IsBenchmarkAvailable =>
        _autoTuningBenchmarkService is not null && _rscBenchmarkService is not null &&
        _bbr2BenchmarkService is not null && _tcpOptionBenchmarkService is not null &&
        _tcpTuningService is not null;

    public bool IsLegacyDiagnosticsAvailable => _legacyNetworkDiagnosticsService is not null;

    public bool IsOperationRunning => DnsSettings.IsBusy || IsBenchmarkRunning || IsApplyingRecommendations ||
        IsLegacyDiagnosticsRunning || IsResettingNic;
    public bool CanApplyRecommendations => IsBenchmarkAvailable && !IsOperationRunning && HasAnyRecommendation;
    private bool HasAnyRecommendation => RecommendedAutoTuningLevel is not null || RecommendedBbr2 is not null ||
        RecommendedRsc is not null || RecommendedEcn is not null || RecommendedRss is not null ||
        RecommendedTimestamps is not null;

    public string AutoTuningRecommendationText => !HasMeasurementRun ? "未計測" :
        RecommendedAutoTuningLevel is { } level ? $"推奨: {level}" :
        IsBenchmarkRunning ? "計測中…" : "推奨: Normal (Windows既定)";

    [ObservableProperty] private bool isBenchmarkRunning;
    [ObservableProperty] private bool hasMeasurementRun;
    [ObservableProperty] private bool isApplyingRecommendations;
    [ObservableProperty] private bool isLegacyDiagnosticsRunning;
    [ObservableProperty] private bool isResettingNic;
    [ObservableProperty] private bool hasLegacyDiagnosticsWarnings;
    [ObservableProperty] private bool canResetSelectedNic;
    [ObservableProperty] private string overallStatusText = string.Empty;
    [ObservableProperty] private string legacyDiagnosticsStatusText = string.Empty;
    [ObservableProperty] private string autoTuningStatusText = string.Empty;
    [ObservableProperty] private AutoTuningLevel? recommendedAutoTuningLevel;
    [ObservableProperty] private BinaryOptimizationRecommendation? recommendedBbr2;
    [ObservableProperty] private BinaryOptimizationRecommendation? recommendedRsc;
    [ObservableProperty] private BinaryOptimizationRecommendation? recommendedEcn;
    [ObservableProperty] private BinaryOptimizationRecommendation? recommendedRss;
    [ObservableProperty] private BinaryOptimizationRecommendation? recommendedTimestamps;

    partial void OnIsBenchmarkRunningChanged(bool value) => NotifyOperationState();
    partial void OnIsApplyingRecommendationsChanged(bool value) => NotifyOperationState();
    partial void OnIsLegacyDiagnosticsRunningChanged(bool value) => NotifyOperationState();
    partial void OnIsResettingNicChanged(bool value) => NotifyOperationState();
    partial void OnHasMeasurementRunChanged(bool value) => OnPropertyChanged(nameof(AutoTuningRecommendationText));
    partial void OnRecommendedAutoTuningLevelChanged(AutoTuningLevel? value) => NotifyRecommendations();
    partial void OnRecommendedBbr2Changed(BinaryOptimizationRecommendation? value) => NotifyRecommendations();
    partial void OnRecommendedRscChanged(BinaryOptimizationRecommendation? value) => NotifyRecommendations();
    partial void OnRecommendedEcnChanged(BinaryOptimizationRecommendation? value) => NotifyRecommendations();
    partial void OnRecommendedRssChanged(BinaryOptimizationRecommendation? value) => NotifyRecommendations();
    partial void OnRecommendedTimestampsChanged(BinaryOptimizationRecommendation? value) => NotifyRecommendations();

    private void NotifyOperationState()
    {
        OnPropertyChanged(nameof(IsOperationRunning));
        OnPropertyChanged(nameof(CanApplyRecommendations));
        OnPropertyChanged(nameof(AutoTuningRecommendationText));
    }

    private void NotifyRecommendations()
    {
        OnPropertyChanged(nameof(CanApplyRecommendations));
        OnPropertyChanged(nameof(AutoTuningRecommendationText));
    }

    [RelayCommand]
    private async Task RunQuickOptimizationAsync()
    {
        await DnsSettings.RunOneClickOptimizationAsync();
        if (_tcpTuningViewModel is not null) await _tcpTuningViewModel.RefreshStateAfterExternalChangeAsync();
    }

    [RelayCommand]
    private async Task RunLegacyNetworkDiagnosticsAsync()
    {
        if (_legacyNetworkDiagnosticsService is null)
        {
            LegacyDiagnosticsStatusText = "使い込んだPC向け診断はWindowsでのみ利用できます";
            return;
        }

        if (DnsSettings.SelectedAdapter is not { } adapter)
        {
            LegacyDiagnosticsStatusText = "診断するネットワークアダプターを選択してください";
            return;
        }

        IsLegacyDiagnosticsRunning = true;
        LegacyNetworkFindings.Clear();
        HasLegacyDiagnosticsWarnings = false;
        CanResetSelectedNic = false;
        LegacyDiagnosticsStatusText = "NIC・ドライバー・古いネットワーク設定を診断しています…";
        try
        {
            var report = await _legacyNetworkDiagnosticsService.DiagnoseAsync(adapter.Id);
            foreach (var finding in report.Findings)
            {
                LegacyNetworkFindings.Add(finding);
            }

            HasLegacyDiagnosticsWarnings = report.HasWarnings;
            CanResetSelectedNic = report.RecommendNicReset;
            LegacyDiagnosticsStatusText = report.HasWarnings
                ? $"{report.Findings.Count(f => f.IsWarning)} 件の確認項目があります。案内に沿って必要なものだけ修復してください"
                : "診断が完了しました。明確なネットワーク残骸は見つかりませんでした";
        }
        catch (Exception ex)
        {
            LegacyDiagnosticsStatusText = $"診断に失敗しました: {ex.Message}";
            LoggerBootstrap.Log.Error("使い込んだPC向けネットワーク診断に失敗しました", ex);
        }
        finally
        {
            IsLegacyDiagnosticsRunning = false;
        }
    }

    [RelayCommand]
    private async Task ResetSelectedNicAdvancedPropertiesAsync()
    {
        if (_legacyNetworkDiagnosticsService is null || DnsSettings.SelectedAdapter is not { } adapter ||
            !CanResetSelectedNic)
        {
            return;
        }

        IsResettingNic = true;
        try
        {
            using var mutationLease = await _networkMutationGate.EnterAsync();
            var result = await _legacyNetworkDiagnosticsService.ResetAdapterAdvancedPropertiesAsync(adapter.Id);
            CommandExecuted?.Invoke(this, result);
            LegacyDiagnosticsStatusText = result.Success
                ? $"{adapter.DisplayName} のNIC詳細設定を工場出荷値へ戻しました。接続が戻ってから再診断してください"
                : $"{adapter.DisplayName} のNIC詳細設定を初期化できませんでした。実行ログを確認してください";
            if (result.Success)
            {
                CanResetSelectedNic = false;
            }
        }
        catch (Exception ex)
        {
            LegacyDiagnosticsStatusText = $"NIC詳細設定の初期化に失敗しました: {ex.Message}";
            LoggerBootstrap.Log.Error("NIC詳細設定の初期化に失敗しました", ex);
        }
        finally
        {
            IsResettingNic = false;
        }
    }

    [RelayCommand]
    private async Task RunAllBenchmarksAsync()
    {
        if (!IsBenchmarkAvailable)
        {
            OverallStatusText = "計測による最適化は Windows でのみ利用できます";
            return;
        }

        if (OperatingSystem.IsWindows() && !WindowsElevationHelper.IsRunningAsAdministrator())
        {
            OverallStatusText = "計測には管理者権限が必要です。Shisui または Visual Studio を管理者として起動してください";
            LoggerBootstrap.Log.Error("自動最適化を開始できません: 管理者権限がありません");
            return;
        }

        _benchmarkCts?.Dispose();
        _benchmarkCts = new CancellationTokenSource();
        var ct = _benchmarkCts.Token;
        IsBenchmarkRunning = true;
        HasMeasurementRun = true;
        ResetMeasurementState();

        try
        {
            await MeasureAutoTuningAsync(ct);
            await MeasureBbr2Async(ct);
            await MeasureRscAsync(ct);
            await MeasureTcpOptionAsync(TcpGlobalOption.EcnCapability, TcpOptionBenchmarkMetric.LoadedPing, EcnGroup, 4, ct);
            await MeasureTcpOptionAsync(TcpGlobalOption.Rss, TcpOptionBenchmarkMetric.DownloadSpeed, RssGroup, 5, ct);
            await MeasureTcpOptionAsync(TcpGlobalOption.Timestamps, TcpOptionBenchmarkMetric.LoadedPing, TimestampsGroup, 6, ct);
            OverallStatusText = HasAnyRecommendation
                ? "6項目の計測が完了しました。推奨内容を確認して一括適用できます"
                : "計測は完了しましたが、適用できる推奨値を判定できませんでした";
        }
        catch (OperationCanceledException)
        {
            ClearRecommendations();
            HasMeasurementRun = false;
            OverallStatusText = "一括計測をキャンセルしました。計測中の設定は開始前に戻しています";
        }
        finally
        {
            IsBenchmarkRunning = false;
            _benchmarkCts.Dispose();
            _benchmarkCts = null;
            if (_tcpTuningViewModel is not null) await _tcpTuningViewModel.RefreshStateAfterExternalChangeAsync();
        }
    }

    private void ResetMeasurementState()
    {
        AutoTuningResults.Clear();
        AutoTuningStatusText = "待機中";
        OverallStatusText = "6項目の一括計測を開始します…";
        foreach (var group in BinaryGroups) group.Reset();
        ClearRecommendations();
    }

    private void ClearRecommendations()
    {
        RecommendedAutoTuningLevel = null;
        RecommendedBbr2 = null;
        RecommendedRsc = null;
        RecommendedEcn = null;
        RecommendedRss = null;
        RecommendedTimestamps = null;
    }

    private async Task MeasureAutoTuningAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        OverallStatusText = "1/6 Auto-Tuning の速度を計測しています…";
        var progress = new Progress<AutoTuningBenchmarkProgress>(p =>
            AutoTuningStatusText = $"計測中: {p.Level} ({p.CompletedCount + 1}/{p.TotalCount})…");
        try
        {
            var results = await _autoTuningBenchmarkService!.RunAsync(BenchmarkLoadSizeBytes, progress, ct);
            var best = results.Where(r => r.Success && r.AverageMbps is not null).OrderByDescending(r => r.AverageMbps).FirstOrDefault();
            foreach (var result in results)
            {
                var text = result.Success && result.AverageMbps is { } avg
                    ? $"平均 {avg:F1} Mbps (最小{result.MinMbps:F1}〜最大{result.MaxMbps:F1}, {result.SampleCount}回平均)"
                    : $"失敗 ({result.ErrorMessage})";
                AutoTuningResults.Add(new(result.Level, text, best is not null && result.Level == best.Level));
            }
            RecommendedAutoTuningLevel = best?.Level ?? AutoTuningLevel.Normal;
            AutoTuningStatusText = best is null ? "有効な速度結果が得られないため、Windows既定のNormalを推奨します" :
                $"平均速度が最も高い {best.Level} を推奨します (開始前の設定へ復元済み)";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AutoTuningStatusText = $"計測に失敗しました: {ex.Message}";
            RecommendedAutoTuningLevel = AutoTuningLevel.Normal;
            LoggerBootstrap.Log.Error("Auto-Tuning ベンチマークに失敗しました", ex);
        }
    }

    private async Task MeasureBbr2Async(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        OverallStatusText = "2/6 BBR2 の負荷時Pingを計測しています…";
        try
        {
            var progress = new Progress<Bbr2BenchmarkProgress>(p =>
                Bbr2Group.StatusText = $"計測中: BBR2 {(p.Enabled ? "有効" : "既定")} ({p.CompletedCount + 1}/{p.TotalCount})…");
            var results = await _bbr2BenchmarkService!.RunAsync(BenchmarkLoadSizeBytes, progress, ct);
            var recommendation = SummarizePingComparison(Bbr2Group, results.Select(r =>
                new BinaryMeasurement(r.Enabled, r.Success, r.AveragePingMs, r.MinPingMs, r.MaxPingMs, r.SampleCount, r.ErrorMessage)), "BBR2", "有効", "既定");
            RecommendedBbr2 = recommendation;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Bbr2Group.FailWithWindowsDefault(ex.Message);
            RecommendedBbr2 = BinaryOptimizationRecommendation.WindowsDefault;
            LoggerBootstrap.Log.Error("BBR2 ベンチマークに失敗しました", ex);
        }
    }

    private async Task MeasureRscAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        OverallStatusText = "3/6 RSC の負荷時Pingを計測しています…";
        try
        {
            var progress = new Progress<RscBenchmarkProgress>(p =>
                RscGroup.StatusText = $"計測中: RSC {(p.Enabled ? "有効" : "無効")} ({p.CompletedCount + 1}/{p.TotalCount})…");
            var results = await _rscBenchmarkService!.RunAsync(BenchmarkLoadSizeBytes, progress, ct);
            var recommendation = SummarizePingComparison(RscGroup, results.Select(r =>
                new BinaryMeasurement(r.Enabled, r.Success, r.AveragePingMs, r.MinPingMs, r.MaxPingMs, r.SampleCount, r.ErrorMessage)), "RSC", "有効", "無効");
            RecommendedRsc = recommendation;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            RscGroup.FailWithWindowsDefault(ex.Message);
            RecommendedRsc = BinaryOptimizationRecommendation.WindowsDefault;
            LoggerBootstrap.Log.Error("RSC ベンチマークに失敗しました", ex);
        }
    }

    private async Task MeasureTcpOptionAsync(
        TcpGlobalOption option, TcpOptionBenchmarkMetric metric, BinaryOptimizationGroup group, int ordinal, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var name = GetOptionName(option);
        OverallStatusText = $"{ordinal}/6 {name} を計測しています…";
        try
        {
            var progress = new Progress<TcpOptionBenchmarkProgress>(p =>
                group.StatusText = $"計測中: {name} {(p.Enabled ? "有効" : "無効")} ({p.CompletedCount + 1}/{p.TotalCount})…");
            var results = await _tcpOptionBenchmarkService!.RunAsync(option, metric, BenchmarkLoadSizeBytes, progress, ct);
            var measurements = results.Select(r => new BinaryMeasurement(r.Enabled, r.Success, r.AverageValue,
                r.MinValue, r.MaxValue, r.SampleCount, r.ErrorMessage));
            var recommendation = metric == TcpOptionBenchmarkMetric.DownloadSpeed
                ? SummarizeSpeedComparison(group, measurements, name)
                : SummarizePingComparison(group, measurements, name, "有効", "無効");
            switch (option)
            {
                case TcpGlobalOption.EcnCapability: RecommendedEcn = recommendation; break;
                case TcpGlobalOption.Rss: RecommendedRss = recommendation; break;
                case TcpGlobalOption.Timestamps: RecommendedTimestamps = recommendation; break;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            group.FailWithWindowsDefault(ex.Message);
            SetTcpOptionRecommendation(option, BinaryOptimizationRecommendation.WindowsDefault);
            LoggerBootstrap.Log.Error($"{name} ベンチマークに失敗しました", ex);
        }
    }

    private void SetTcpOptionRecommendation(TcpGlobalOption option, BinaryOptimizationRecommendation recommendation)
    {
        switch (option)
        {
            case TcpGlobalOption.EcnCapability: RecommendedEcn = recommendation; break;
            case TcpGlobalOption.Rss: RecommendedRss = recommendation; break;
            case TcpGlobalOption.Timestamps: RecommendedTimestamps = recommendation; break;
        }
    }

    private static BinaryOptimizationRecommendation SummarizePingComparison(
        BinaryOptimizationGroup group, IEnumerable<BinaryMeasurement> source, string name,
        string enabledLabel, string disabledLabel)
    {
        var results = source.ToList();
        var enabled = results.FirstOrDefault(r => r.Enabled && r.Success && r.Average is not null);
        var disabled = results.FirstOrDefault(r => !r.Enabled && r.Success && r.Average is not null);
        var difference = enabled?.Average is { } ep && disabled?.Average is { } dp ? Math.Abs(ep - dp) : (double?)null;
        var meaningful = difference is >= PingSignificanceMs;
        var bestEnabled = meaningful && enabled!.Average < disabled!.Average;
        foreach (var result in results)
        {
            var text = result.Success && result.Average is { } avg
                ? $"平均 {avg:F1} ms (最小{result.Min:F1}〜最大{result.Max:F1}, {result.SampleCount}回平均)"
                : $"失敗 ({result.ErrorMessage})";
            group.Results.Add(new($"{name} {(result.Enabled ? enabledLabel : disabledLabel)}", text, meaningful && result.Enabled == bestEnabled));
        }
        if (difference is null)
        {
            group.RecommendationText = "推奨: Windows既定値";
            group.StatusText = "両方の計測結果が揃わないため、Windows既定値を推奨します";
            return BinaryOptimizationRecommendation.WindowsDefault;
        }
        if (!meaningful)
        {
            group.RecommendationText = "推奨: Windows既定値";
            group.StatusText = "差が1 ms未満のため、Windows既定値を推奨します (開始前へ復元済み)";
            return BinaryOptimizationRecommendation.WindowsDefault;
        }
        group.RecommendationText = $"推奨: {name} {(bestEnabled ? enabledLabel : disabledLabel)}";
        group.StatusText = $"{(bestEnabled ? enabledLabel : disabledLabel)}の方が平均 {difference.Value:F1} ms低いため推奨します (開始前へ復元済み)";
        return bestEnabled ? BinaryOptimizationRecommendation.Enabled : BinaryOptimizationRecommendation.Disabled;
    }

    private static BinaryOptimizationRecommendation SummarizeSpeedComparison(
        BinaryOptimizationGroup group, IEnumerable<BinaryMeasurement> source, string name)
    {
        var results = source.ToList();
        var enabled = results.FirstOrDefault(r => r.Enabled && r.Success && r.Average is not null);
        var disabled = results.FirstOrDefault(r => !r.Enabled && r.Success && r.Average is not null);
        var max = Math.Max(enabled?.Average ?? 0, disabled?.Average ?? 0);
        var difference = enabled?.Average is { } es && disabled?.Average is { } ds ? Math.Abs(es - ds) : (double?)null;
        var meaningful = difference is { } d && max > 0 && d / max >= SpeedSignificanceRatio;
        var bestEnabled = meaningful && enabled!.Average > disabled!.Average;
        foreach (var result in results)
        {
            var text = result.Success && result.Average is { } avg
                ? $"平均 {avg:F1} Mbps (最小{result.Min:F1}〜最大{result.Max:F1}, {result.SampleCount}回平均)"
                : $"失敗 ({result.ErrorMessage})";
            group.Results.Add(new($"{name} {(result.Enabled ? "有効" : "無効")}", text, meaningful && result.Enabled == bestEnabled));
        }
        if (difference is null)
        {
            group.RecommendationText = "推奨: Windows既定値";
            group.StatusText = "両方の計測結果が揃わないため、Windows既定値を推奨します";
            return BinaryOptimizationRecommendation.WindowsDefault;
        }
        if (!meaningful)
        {
            group.RecommendationText = "推奨: Windows既定値";
            group.StatusText = "速度差が3%未満のため、Windows既定値を推奨します (開始前へ復元済み)";
            return BinaryOptimizationRecommendation.WindowsDefault;
        }
        group.RecommendationText = $"推奨: {name} {(bestEnabled ? "有効" : "無効")}";
        group.StatusText = $"{(bestEnabled ? "有効" : "無効")}の方が平均速度が高いため推奨します (開始前へ復元済み)";
        return bestEnabled ? BinaryOptimizationRecommendation.Enabled : BinaryOptimizationRecommendation.Disabled;
    }

    [RelayCommand] private void CancelBenchmarks() => _benchmarkCts?.Cancel();

    [RelayCommand]
    private async Task ApplyRecommendationsAsync()
    {
        if (_tcpTuningService is null || !HasAnyRecommendation)
        {
            OverallStatusText = "適用できる推奨設定がありません";
            return;
        }
        IsApplyingRecommendations = true;
        try
        {
            using var lease = await _networkMutationGate.EnterAsync();
            var results = new List<CommandExecutionResult>();
            if (RecommendedAutoTuningLevel is { } auto) results.Add(await _tcpTuningService.SetAutoTuningLevelAsync(auto));
            if (RecommendedBbr2 is { } bbr2)
                results.AddRange(bbr2 == BinaryOptimizationRecommendation.Enabled
                    ? await _tcpTuningService.EnableBbr2Async()
                    : await _tcpTuningService.RevertBbr2ToDefaultAsync());
            if (RecommendedRsc is { } rsc) results.Add(await ApplyTcpOptionRecommendationAsync(TcpGlobalOption.Rsc, rsc));
            if (RecommendedEcn is { } ecn) results.Add(await ApplyTcpOptionRecommendationAsync(TcpGlobalOption.EcnCapability, ecn));
            if (RecommendedRss is { } rss) results.Add(await ApplyTcpOptionRecommendationAsync(TcpGlobalOption.Rss, rss));
            if (RecommendedTimestamps is { } ts) results.Add(await ApplyTcpOptionRecommendationAsync(TcpGlobalOption.Timestamps, ts));
            foreach (var result in results) CommandExecuted?.Invoke(this, result);
            OverallStatusText = results.Count > 0 && results.All(r => r.Success)
                ? "計測で得た推奨設定を一括適用しました"
                : "一部の推奨設定を適用できませんでした。実行ログを確認してください";
        }
        finally
        {
            IsApplyingRecommendations = false;
            if (_tcpTuningViewModel is not null) await _tcpTuningViewModel.RefreshStateAfterExternalChangeAsync();
        }
    }

    private Task<CommandExecutionResult> ApplyTcpOptionRecommendationAsync(
        TcpGlobalOption option,
        BinaryOptimizationRecommendation recommendation) => recommendation switch
    {
        BinaryOptimizationRecommendation.WindowsDefault =>
            _tcpTuningService!.RevertTcpGlobalOptionToDefaultAsync(option),
        BinaryOptimizationRecommendation.Enabled =>
            _tcpTuningService!.SetTcpGlobalOptionAsync(option, true),
        BinaryOptimizationRecommendation.Disabled =>
            _tcpTuningService!.SetTcpGlobalOptionAsync(option, false),
        _ => throw new ArgumentOutOfRangeException(nameof(recommendation)),
    };

    private static string GetOptionName(TcpGlobalOption option) => option switch
    {
        TcpGlobalOption.EcnCapability => "ECN",
        TcpGlobalOption.Rss => "RSS",
        TcpGlobalOption.Timestamps => "TCPタイムスタンプ",
        _ => option.ToString(),
    };

    private sealed record BinaryMeasurement(bool Enabled, bool Success, double? Average, double? Min,
        double? Max, int SampleCount, string? ErrorMessage);
}
