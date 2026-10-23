using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>呼び出し元が共有ネットワーク変更 lease を保持した状態で利用する IP 初期化。</summary>
public interface IIpStackResetService
{
    Task<IReadOnlyList<CommandExecutionResult>> ResetAsync(CancellationToken ct = default);
}
