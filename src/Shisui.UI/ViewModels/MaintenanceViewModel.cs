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

    public event EventHandler<CommandExecutionResult>? CommandExecuted;

    public IReadOnlyList<MaintenanceCategoryGroup> Categories { get; }

    [ObservableProperty]
    private string statusText = string.Empty;

    public MaintenanceViewModel(INetworkMaintenanceService maintenanceService)
    {
        _maintenanceService = maintenanceService;
        Categories = maintenanceService.GetAvailableCommands()
            .GroupBy(c => c.Category)
            .Select(g => new MaintenanceCategoryGroup(g.Key, g.Select(d => new MaintenanceCommandItemViewModel(d, RunAsync)).ToList()))
            .ToList();
    }

    private async Task RunAsync(MaintenanceCommandItemViewModel item)
    {
        item.IsRunning = true;
        try
        {
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
}
