using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>
/// 「任意実行」ネットワークメンテナンスコマンド群 (nbtstat / ipconfig / netsh 各種等)。
/// </summary>
public interface INetworkMaintenanceService
{
    IReadOnlyList<MaintenanceCommandDefinition> GetAvailableCommands();

    /// <summary>「まとめて実行」に対応するカテゴリ → ボタンラベル。非対応カテゴリは含まれない。</summary>
    IReadOnlyDictionary<string, string> GetBatchableCategoryLabels();

    Task<CommandExecutionResult> RunAsync(string commandId, CancellationToken ct = default);
}
