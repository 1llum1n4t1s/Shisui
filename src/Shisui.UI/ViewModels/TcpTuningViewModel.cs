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
    INetworkMutationGate networkMutationGate) : ObservableObject
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

    /// <summary>TCP 手動操作の排他制御用。計測側との競合は共有の
    /// <see cref="INetworkMutationGate"/> がプロセス全体で防止する。</summary>
    public bool IsOperationRunning => IsBusy;

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
            using var mutationLease = await networkMutationGate.EnterAsync();
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
    private async Task ResetAllTcpSettingsAsync() => await RunOneAsync(
        tcpTuningService.ResetAllTcpSettingsToDefaultAsync,
        "TCP スタック全体を Windows の既定値に戻しました");

    [RelayCommand]
    private async Task RevertGlobalOptionsAsync() => await RunManyAsync(
        tcpTuningService.RevertGlobalOptionsToDefaultAsync,
        "TCP グローバル詳細設定を Windows の既定値に戻しました");

    [RelayCommand]
    private async Task RevertLegacyTcpRegistryTweaksAsync() => await RunOneAsync(
        tcpTuningService.RevertLegacyTcpRegistryTweaksToDefaultAsync,
        "TCP ACK / Nagle 関連設定を既定値に戻しました。反映のため PC を再起動してください");

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
            using var mutationLease = await networkMutationGate.EnterAsync();
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
            using var mutationLease = await networkMutationGate.EnterAsync();
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

    private async Task RunOneAsync(Func<CancellationToken, Task<CommandExecutionResult>> action, string successMessage)
    {
        IsBusy = true;
        try
        {
            using var mutationLease = await networkMutationGate.EnterAsync();
            var result = await action(CancellationToken.None);
            CommandExecuted?.Invoke(this, result);
            StatusText = result.Success ? successMessage : "コマンドが失敗しました。ログを確認してください";
        }
        finally
        {
            IsBusy = false;
        }

        await LoadStateAsync();
    }

    /// <summary>自動最適化タブなど、外部の設定操作後に手動調整タブの状態表示を同期する。</summary>
    internal Task RefreshStateAfterExternalChangeAsync() => LoadStateAsync();

    private async Task RunManyAsync(Func<CancellationToken, Task<IReadOnlyList<CommandExecutionResult>>> action, string successMessage)
    {
        IsBusy = true;
        try
        {
            using var mutationLease = await networkMutationGate.EnterAsync();
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
