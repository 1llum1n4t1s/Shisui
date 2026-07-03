using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>
/// 「任意実行」ネットワークメンテナンスコマンド群 (nbtstat / ipconfig / netsh 各種等)。
/// </summary>
public interface INetworkMaintenanceService
{
    IReadOnlyList<MaintenanceCommandDefinition> GetAvailableCommands();

    Task<CommandExecutionResult> RunAsync(string commandId, CancellationToken ct = default);
}
