using CommunityToolkit.Mvvm.ComponentModel;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.UI.ViewModels;

/// <summary>
/// 任意実行ネットワークメンテナンスコマンド一覧タブ (Windows 専用)。
/// </summary>
public partial class MaintenanceViewModel : ObservableObject
{
    private readonly INetworkMaintenanceService _maintenanceService;
    private readonly INetworkMutationGate _networkMutationGate;

    public event EventHandler<CommandExecutionResult>? CommandExecuted;

    public IReadOnlyList<MaintenanceCategoryGroup> Categories { get; }

    [ObservableProperty]
    private string statusText = string.Empty;

    public MaintenanceViewModel(
        INetworkMaintenanceService maintenanceService,
        INetworkMutationGate networkMutationGate)
    {
        _maintenanceService = maintenanceService;
        _networkMutationGate = networkMutationGate;
        var batchLabels = maintenanceService.GetBatchableCategoryLabels();
        Categories = maintenanceService.GetAvailableCommands()
            .GroupBy(c => c.Category)
            .Select(g => new MaintenanceCategoryGroup(
                g.Key,
                g.Select(d => new MaintenanceCommandItemViewModel(d, RunAsync)).ToList(),
                batchLabels.TryGetValue(g.Key, out var label) ? label : null,
                RunAllAsync))
            .ToList();
    }

    private async Task RunAsync(MaintenanceCommandItemViewModel item)
    {
        item.IsRunning = true;
        try
        {
            using var mutationLease = await _networkMutationGate.EnterAsync();
            var result = await _maintenanceService.RunAsync(item.Definition.Id);
            CommandExecuted?.Invoke(this, result);
            StatusText = result.Success
                ? $"{item.Definition.Label} が完了しました"
                : $"{item.Definition.Label} が失敗しました (ログを確認してください)";
        }
        finally
        {
            item.IsRunning = false;
        }
    }

    /// <summary>
    /// カテゴリ内のコマンドをカタログ並び順で逐次実行する (IP 再取得なら 解放 → 再取得 の順)。
    /// 個々の結果はログに流し、最後に成功/失敗件数をまとめて表示する。
    /// </summary>
    private async Task RunAllAsync(MaintenanceCategoryGroup group)
    {
        group.IsRunningAll = true;
        try
        {
            // カテゴリ一括実行の途中へ別のTCP/DNS操作を割り込ませない。
            using var mutationLease = await _networkMutationGate.EnterAsync();
            var failed = 0;
            foreach (var item in group.Items)
            {
                item.IsRunning = true;
                try
                {
                    var result = await _maintenanceService.RunAsync(item.Definition.Id);
                    CommandExecuted?.Invoke(this, result);
                    if (!result.Success)
                    {
                        failed++;
                    }
                }
                finally
                {
                    item.IsRunning = false;
                }
            }

            StatusText = failed == 0
                ? $"「{group.Name}」を順に実行しました ({group.Items.Count} 件)"
                : $"「{group.Name}」を実行しました ({group.Items.Count} 件中 {failed} 件失敗。ログを確認してください)";
        }
        finally
        {
            group.IsRunningAll = false;
        }
    }
}
