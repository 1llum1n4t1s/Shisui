using Shisui.Core.Models;

namespace Shisui.Core.Interfaces;

/// <summary>
/// BBR2 輻輳制御・TCP グローバル設定の調整 (Windows 専用機能)。
/// macOS では DI 登録されず、UI 側がタブごと非表示にする。
/// </summary>
public interface ITcpTuningService
{
    Task<IReadOnlyList<CommandExecutionResult>> EnableBbr2Async(CancellationToken ct = default);

    Task<IReadOnlyList<CommandExecutionResult>> RevertBbr2ToDefaultAsync(CancellationToken ct = default);

    Task<CommandExecutionResult> SetTcpGlobalOptionAsync(TcpGlobalOption option, bool enabled, CancellationToken ct = default);

    Task<CommandExecutionResult> ShowTcpGlobalStatusAsync(CancellationToken ct = default);

    /// <summary>現在の BBR2 適用状況・各 TCP グローバルオプションの状態を取得する。</summary>
    Task<TcpSettingsSnapshot> GetCurrentStateAsync(CancellationToken ct = default);
}

public enum TcpGlobalOption
{
    Rsc,
    EcnCapability,
    Timestamps,
    Rss,
    FastOpen,
}
