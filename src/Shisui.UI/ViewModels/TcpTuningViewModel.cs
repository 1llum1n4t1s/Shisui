using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.UI.ViewModels;

/// <summary>
/// BBR2 輻輳制御・TCP グローバル設定タブ (Windows 専用)。
/// 各設定の「現在の状態」を PowerShell から取得して表示し、操作のたびに自動で再取得する。
/// </summary>
public partial class TcpTuningViewModel(ITcpTuningService tcpTuningService) : ObservableObject
{
    public event EventHandler<CommandExecutionResult>? CommandExecuted;

    [ObservableProperty]
    private bool isBusy;

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

    public void Initialize() => _ = RefreshStateAsync();

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
        }
        catch
        {
            // 状態取得の失敗は致命的ではない (設定操作自体はできる) ので握りつぶし、不明表示にする。
            Bbr2StateText = RscStateText = EcnStateText = TimestampsStateText = RssStateText = FastOpenStateText = "⚪ 不明";
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
