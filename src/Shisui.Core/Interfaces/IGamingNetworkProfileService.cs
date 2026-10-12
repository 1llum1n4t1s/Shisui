using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>対応する物理 Ethernet / Wi-Fi NIC のゲーム向け遅延設定を、安全に適用・復元する。</summary>
public interface IGamingNetworkProfileService
{
    Task<IReadOnlyList<CommandExecutionResult>> ApplyAsync(
        string adapterName,
        CancellationToken ct = default);

    /// <summary>
    /// 呼び出し元が同じ共有ゲートの lease を取得・保持済みのときだけ使用する。
    /// lease の取得と解放は呼び出し元の責務とする。
    /// </summary>
    Task<IReadOnlyList<CommandExecutionResult>> ApplyWithinMutationLeaseAsync(
        string adapterName,
        CancellationToken ct = default);

    Task<IReadOnlyList<CommandExecutionResult>> RestoreAsync(
        string adapterName,
        CancellationToken ct = default);
}
