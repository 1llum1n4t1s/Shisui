using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.UI.ViewModels;

/// <summary>
/// BBR2 輻輳制御・TCP グローバル設定タブ (Windows 専用)。
/// 各設定の「現在の状態」を PowerShell から取得して表示し、操作のたびに自動で再取得する。
/// </summary>
public partial class TcpTuningViewModel(
    ITcpTuningService tcpTuningService,
    INetworkAdapterService adapterService,
    IAutoTuningBenchmarkService autoTuningBenchmarkService) : ObservableObject
{
    public event EventHandler<CommandExecutionResult>? CommandExecuted;

    [ObservableProperty]
    private bool isBusy;

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsOperationRunning));

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private string globalStatusOutput = string.Empty;

    // 各設定の現在状態バッジ (🟢有効 / 🔴無効 / 🟡一部のみ / ⚪不明 等)。
    [ObservableProperty]
    private string bbr2StateText = "🔄 確認中…";

    [ObservableProperty]
    private string rscStateText = "🔄 確認中…";

    [ObservableProperty]
    private string ecnStateText = "🔄 確認中…";

    [ObservableProperty]
    private string timestampsStateText = "🔄 確認中…";

    [ObservableProperty]
    private string rssStateText = "🔄 確認中…";

    [ObservableProperty]
    private string fastOpenStateText = "🔄 確認中…";

    [ObservableProperty]
    private string autoTuningStateText = "🔄 確認中…";

    [ObservableProperty]
    private AutoTuningLevel selectedAutoTuningLevel = AutoTuningLevel.Normal;

    public IReadOnlyList<AutoTuningLevel> AutoTuningLevels { get; } = Enum.GetValues<AutoTuningLevel>();

    // 固定 5MB。既定 20MB だと、Disabled 等の auto-tuning 制限レベルで受信ウィンドウが縮小された際に
    // 海外の計測先 (Hetzner) への実効スループットが数百 KB/s まで落ち込み、MeasureOnceAsync の
    // HttpClient タイムアウト (30秒) を超えて計測1回あたりの所要時間が跳ね上がっていた (2026-07-06 実測で特定)。
    // タイムアウト内に収まるサイズへ固定し、ユーザーが調整できないようにした。
    private const int BenchmarkTestSizeBytes = 5_000_000;

    [ObservableProperty]
    private int benchmarkSamplesPerLevel = 5;

    [ObservableProperty]
    private bool isBenchmarkRunning;

    partial void OnIsBenchmarkRunningChanged(bool value) => OnPropertyChanged(nameof(IsOperationRunning));

    /// <summary>BBR2・TCPオプション・Auto-Tuning設定ボタンの排他制御用。ベンチマーク実行中に他の
    /// netsh操作を許すと、ベンチマーク終了時の設定復元(finally)と競合し、ユーザーの手動設定が
    /// 静かに上書きされて消える(2026-07-06 /rere レビューで発見)。IsBusy と IsBenchmarkRunning は
    /// 独立したフラグのままにし、UI側の判定だけをここに集約する。</summary>
    public bool IsOperationRunning => IsBusy || IsBenchmarkRunning;

    [ObservableProperty]
    private string benchmarkStatusText = string.Empty;

    public ObservableCollection<AutoTuningBenchmarkRow> BenchmarkResults { get; } = [];

    private CancellationTokenSource? benchmarkCts;

    [ObservableProperty]
    private bool isMtuBusy;

    [ObservableProperty]
    private NetworkAdapterInfo? selectedAdapter;

    [ObservableProperty]
    private int mtuValue = 1500;

    [ObservableProperty]
    private string mtuStateText = string.Empty;

    public ObservableCollection<NetworkAdapterInfo> Adapters { get; } = [];

    public void Initialize()
    {
        _ = RefreshStateAsync();
        _ = LoadAdaptersAsync();
    }

    [RelayCommand]
    private async Task LoadAdaptersAsync()
    {
        IsMtuBusy = true;
        try
        {
            var adapters = await adapterService.GetAdaptersAsync();
            Adapters.Clear();
            foreach (var adapter in adapters)
            {
                Adapters.Add(adapter);
            }

            SelectedAdapter ??= Adapters.FirstOrDefault();
        }
        finally
        {
            IsMtuBusy = false;
        }
    }

    partial void OnSelectedAdapterChanged(NetworkAdapterInfo? value) => _ = RefreshMtuStateAsync();

    [RelayCommand]
    private async Task SetMtuAsync()
    {
        if (SelectedAdapter is null)
        {
            StatusText = "MTU を設定するアダプタを選択してください";
            return;
        }

        IsMtuBusy = true;
        try
        {
            var results = await tcpTuningService.SetMtuAsync(SelectedAdapter.Id, MtuValue);
            foreach (var result in results)
            {
                CommandExecuted?.Invoke(this, result);
            }

            StatusText = results.All(r => r.Success)
                ? $"{SelectedAdapter.DisplayName} の MTU を {MtuValue} に設定しました"
                : "一部のコマンドが失敗しました。ログを確認してください";
        }
        finally
        {
            IsMtuBusy = false;
        }

        await RefreshMtuStateAsync();
    }

    private async Task RefreshMtuStateAsync()
    {
        if (SelectedAdapter is null)
        {
            MtuStateText = string.Empty;
            return;
        }

        try
        {
            var mtu = await tcpTuningService.GetMtuAsync(SelectedAdapter.Id);
            if (mtu is not null)
            {
                MtuStateText = $"現在の MTU: {mtu}";
                MtuValue = mtu.Value;
            }
            else
            {
                MtuStateText = "⚪ 取得できませんでした";
            }
        }
        catch
        {
            MtuStateText = "⚪ 取得できませんでした";
        }
    }

    [RelayCommand]
    private async Task EnableBbr2Async() => await RunManyAsync(
        tcpTuningService.EnableBbr2Async, "BBR2 を有効化しました");

    [RelayCommand]
    private async Task RevertBbr2Async() => await RunManyAsync(
        tcpTuningService.RevertBbr2ToDefaultAsync, "BBR2 設定を既定値に戻しました");

    [RelayCommand]
    private async Task SetOptionAsync(string parameter)
    {
        // "Rsc:true" のような "TcpGlobalOption:enabled" 形式で XAML から渡す
        var parts = parameter.Split(':');
        var option = Enum.Parse<TcpGlobalOption>(parts[0]);
        var enabled = bool.Parse(parts[1]);

        IsBusy = true;
        try
        {
            var result = await tcpTuningService.SetTcpGlobalOptionAsync(option, enabled);
            CommandExecuted?.Invoke(this, result);
            StatusText = result.Success ? $"{option} を {(enabled ? "有効" : "無効")} にしました" : "コマンドが失敗しました";
        }
        finally
        {
            IsBusy = false;
        }

        await LoadStateAsync();
    }

    [RelayCommand]
    private async Task SetAutoTuningLevelAsync()
    {
        IsBusy = true;
        try
        {
            var result = await tcpTuningService.SetAutoTuningLevelAsync(SelectedAutoTuningLevel);
            CommandExecuted?.Invoke(this, result);
            StatusText = result.Success
                ? $"受信ウィンドウ自動調整を {SelectedAutoTuningLevel} にしました"
                : "コマンドが失敗しました";
        }
        finally
        {
            IsBusy = false;
        }

        await LoadStateAsync();
    }

    [RelayCommand]
    private async Task RunAutoTuningBenchmarkAsync()
    {
        benchmarkCts = new CancellationTokenSource();
        IsBenchmarkRunning = true;
        BenchmarkResults.Clear();
        BenchmarkStatusText = "計測を開始します…";

        var progress = new Progress<AutoTuningBenchmarkProgress>(p =>
            BenchmarkStatusText = $"計測中: {p.Level} ({p.CompletedCount + 1}/{p.TotalCount})…");

        IReadOnlyList<AutoTuningBenchmarkResult> results = [];
        try
        {
            results = await autoTuningBenchmarkService.RunAsync(BenchmarkTestSizeBytes, BenchmarkSamplesPerLevel, progress, benchmarkCts.Token);
        }
        catch (OperationCanceledException)
        {
            BenchmarkStatusText = "計測をキャンセルしました (設定は元に戻しています)";
        }
        finally
        {
            IsBenchmarkRunning = false;
            benchmarkCts = null;
        }

        // 各レベルを切り替えた後、計測開始前のレベルに戻っているはずなので状態バッジを再取得する
        // (SelectedAutoTuningLevel も一旦ここで実際の値に戻る。ベスト値の選択はこの後で行う)。
        await LoadStateAsync();

        if (results.Count == 0)
        {
            return;
        }

        var bestMbps = results.Where(r => r.Success).Select(r => r.ThroughputMbps).DefaultIfEmpty().Max();
        foreach (var result in results)
        {
            var throughputText = result.Success && result.ThroughputMbps is { } mbps
                ? $"平均 {mbps:F1} Mbps (最小{result.MinThroughputMbps:F1}〜最大{result.MaxThroughputMbps:F1}, {result.SampleCount}回平均)"
                : $"失敗 ({result.ErrorMessage})";
            BenchmarkResults.Add(new AutoTuningBenchmarkRow(result.Level, throughputText, result.Success && result.ThroughputMbps == bestMbps));
        }

        var best = results.Where(r => r.Success).OrderByDescending(r => r.ThroughputMbps).FirstOrDefault();
        BenchmarkStatusText = best is not null
            ? $"計測が完了しました。{best.Level} が最も速かったため選択しました。「設定」を押すと適用されます"
            : "計測に失敗しました。ネットワーク接続を確認してください";

        if (best is not null)
        {
            SelectedAutoTuningLevel = best.Level;
        }
    }

    [RelayCommand]
    private void CancelAutoTuningBenchmark() => benchmarkCts?.Cancel();

    [RelayCommand]
    private async Task RefreshStateAsync()
    {
        IsBusy = true;
        try
        {
            await LoadStateAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ShowGlobalStatusAsync()
    {
        IsBusy = true;
        try
        {
            var result = await tcpTuningService.ShowTcpGlobalStatusAsync();
            CommandExecuted?.Invoke(this, result);
            GlobalStatusOutput = result.Success ? result.StandardOutput : result.StandardError;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadStateAsync()
    {
        try
        {
            var snapshot = await tcpTuningService.GetCurrentStateAsync();
            Bbr2StateText = FormatBbr2(snapshot.Bbr2);
            RscStateText = FormatOption(snapshot.GetOptionValue(TcpGlobalOption.Rsc));
            EcnStateText = FormatOption(snapshot.GetOptionValue(TcpGlobalOption.EcnCapability));
            TimestampsStateText = FormatOption(snapshot.GetOptionValue(TcpGlobalOption.Timestamps));
            RssStateText = FormatOption(snapshot.GetOptionValue(TcpGlobalOption.Rss));
            FastOpenStateText = FormatFastOpen(snapshot.GetOptionValue(TcpGlobalOption.FastOpen));
            AutoTuningStateText = FormatAutoTuningLevel(snapshot.AutoTuningLevel);
            if (Enum.TryParse<AutoTuningLevel>(snapshot.AutoTuningLevel, ignoreCase: true, out var parsedLevel))
            {
                SelectedAutoTuningLevel = parsedLevel;
            }
        }
        catch
        {
            // 状態取得の失敗は致命的ではない (設定操作自体はできる) ので握りつぶし、不明表示にする。
            Bbr2StateText = RscStateText = EcnStateText = TimestampsStateText = RssStateText = FastOpenStateText = AutoTuningStateText = "⚪ 不明";
        }
    }

    private static string FormatBbr2(Bbr2Status status) => status switch
    {
        Bbr2Status.Enabled => "🟢 有効",
        Bbr2Status.Disabled => "🔴 無効 (既定)",
        Bbr2Status.Partial => "🟡 一部のみ有効",
        _ => "⚪ 不明",
    };

    private static string FormatOption(string rawValue) => rawValue.ToUpperInvariant() switch
    {
        "ENABLED" => "🟢 有効",
        "DISABLED" => "🔴 無効",
        "ALLOWED" => "🟢 許可",
        "DEFAULT" => "⚪ 既定",
        _ => "⚪ 不明",
    };

    // FastOpen は Windows の照会 API に公開されておらず取得できない。誤解を避けて明示する。
    private static string FormatFastOpen(string rawValue) =>
        string.IsNullOrEmpty(rawValue) ? "⚪ 取得非対応" : FormatOption(rawValue);

    private static string FormatAutoTuningLevel(string rawValue) => rawValue.ToUpperInvariant() switch
    {
        "NORMAL" => "🟢 Normal (既定)",
        "DISABLED" => "🔴 Disabled",
        "RESTRICTED" => "🟡 Restricted",
        "HIGHLYRESTRICTED" => "🟡 HighlyRestricted",
        "EXPERIMENTAL" => "🟡 Experimental",
        _ => "⚪ 不明",
    };

    private async Task RunManyAsync(Func<CancellationToken, Task<IReadOnlyList<CommandExecutionResult>>> action, string successMessage)
    {
        IsBusy = true;
        try
        {
            var results = await action(CancellationToken.None);
            foreach (var result in results)
            {
                CommandExecuted?.Invoke(this, result);
            }

            StatusText = results.All(r => r.Success) ? successMessage : "一部のコマンドが失敗しました。ログを確認してください";
        }
        finally
        {
            IsBusy = false;
        }

        await LoadStateAsync();
    }
}
