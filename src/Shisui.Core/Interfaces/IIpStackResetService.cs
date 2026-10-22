using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>呼び出し元が共有ネットワーク変更 lease を保持した状態で利用する IP 初期化と復旧。</summary>
public interface IIpStackResetService
{
    bool IsRecoveryRequired { get; }
    Task<IReadOnlyList<CommandExecutionResult>> ResetAndRestoreAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CommandExecutionResult>> RecoverPendingAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CommandExecutionResult>> CompleteOptimizationAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CommandExecutionResult>> RestoreOriginalAsync(CancellationToken ct = default);
}
