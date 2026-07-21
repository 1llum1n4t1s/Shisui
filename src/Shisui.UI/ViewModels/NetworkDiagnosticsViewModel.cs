using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.UI.ViewModels;

/// <summary>
/// 任意ホストへの ping / トレースルートを行う診断タブ (Windows/macOS 両対応)。
/// </summary>
public partial class NetworkDiagnosticsViewModel(INetworkDiagnosticsService diagnosticsService) : ObservableObject
{
    private const int PingCount = 4;
    private const int MaxHops = 30;

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

    [RelayCommand]
    private async Task PingAsync()
    {
        var target = Host.Trim();
        if (target.Length == 0)
        {
            StatusText = "ホスト名または IP アドレスを入力してください";
            return;
        }

        IsBusy = true;
        PingResultText = string.Empty;
        try
        {
            var result = await diagnosticsService.PingAsync(target, PingCount);
            PingResultText = result.Success
                ? $"🟢 応答あり: {result.Received}/{result.Sent} 件 (平均 {result.AverageRoundtripMs:F0} ms)"
                : $"🔴 応答なし: {result.Received}/{result.Sent} 件";
            StatusText = $"{target} への ping が完了しました";
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
