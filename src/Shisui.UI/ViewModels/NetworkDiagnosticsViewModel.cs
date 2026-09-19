using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services;

namespace Shisui.UI.ViewModels;

/// <summary>
/// 任意ホストへの ping / トレースルートを行う診断タブ (Windows/macOS 両対応)。
/// </summary>
public partial class NetworkDiagnosticsViewModel(INetworkDiagnosticsService diagnosticsService) : ObservableObject
{
    private const int MaxHops = 30;

    public IReadOnlyList<int> PingCounts { get; } = [4, 30, 100];

    [ObservableProperty]
    private int pingCount = 30;

    [ObservableProperty]
    private string host = string.Empty;

    [ObservableProperty]
    private NetworkDiagnosticTargetPreset? selectedTargetPreset;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private string pingResultText = string.Empty;

    public ObservableCollection<TraceRouteHop> TraceRouteHops { get; } = [];
    public IReadOnlyList<NetworkDiagnosticTargetPreset> TargetPresets => NetworkDiagnosticTargetCatalog.All;

    partial void OnSelectedTargetPresetChanged(NetworkDiagnosticTargetPreset? value)
    {
        if (value is not null)
        {
            Host = value.Host;
        }
    }

    partial void OnHostChanged(string value)
    {
        if (SelectedTargetPreset is { } selected &&
            !string.Equals(value.Trim(), selected.Host, StringComparison.OrdinalIgnoreCase))
        {
            SelectedTargetPreset = null;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task PingAsync(CancellationToken cancellationToken)
    {
        var target = Host.Trim();
        if (target.Length == 0)
        {
            StatusText = "ホスト名または IP アドレスを入力してください";
            return;
        }

        var count = PingCount;
        if (!PingCounts.Contains(count))
        {
            StatusText = "測定回数は4・30・100回から選択してください";
            return;
        }

        IsBusy = true;
        PingResultText = string.Empty;
        StatusText = $"{target} へ {count} 回測定しています…（約 {count} 秒以上）";
        using var operation = LoggerBootstrap.BeginOperation("遅延・損失測定", target);
        try
        {
            var result = await diagnosticsService.PingAsync(target, count, cancellationToken);
            PingResultText = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} / {target}\n" +
                $"応答 {result.Received}/{result.Sent} / 損失率 {Format(result.LossPercent, "%")}\n" +
                $"平均 {Format(result.AverageRoundtripMs)} / 最小 {Format(result.MinimumRoundtripMs)} / 最大 {Format(result.MaximumRoundtripMs)}\n" +
                $"p95 {Format(result.P95RoundtripMs)} / ジッター {Format(result.JitterMs)}";
            LoggerBootstrap.Log.Info($"LatencyMeasurement target={target} requested={count}\n{PingResultText}\n" +
                "ICMP・OSの経路選択を使用。NIC指定なし。ゲーム通信の実測値ではありません。");
            StatusText = result.Sent == 0
                ? $"測定を開始できませんでした: {result.RawOutput}"
                : $"{target} への ping が完了しました";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText = "測定を中止しました。未完了の結果は比較に使用しません";
        }
        catch (Exception ex)
        {
            StatusText = $"ping に失敗しました: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Format(double? value, string unit = "ms") =>
        value is { } number ? $"{number:F2} {unit}" : "取得なし";

    [RelayCommand]
    private async Task TraceRouteAsync()
    {
        var target = Host.Trim();
        if (target.Length == 0)
        {
            StatusText = "ホスト名または IP アドレスを入力してください";
            return;
        }

        IsBusy = true;
        TraceRouteHops.Clear();
        try
        {
            var result = await diagnosticsService.TraceRouteAsync(target, MaxHops);
            foreach (var hop in result.Hops)
            {
                TraceRouteHops.Add(hop);
            }

            StatusText = result.Success
                ? $"{target} までの経路を {result.Hops.Count} ホップ取得しました"
                : $"{target} までの経路を取得できませんでした";
        }
        catch (Exception ex)
        {
            StatusText = $"トレースルートに失敗しました: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
