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

    /// <summary>受信ウィンドウ自動調整 (auto-tuning) レベルを設定する。</summary>
    Task<CommandExecutionResult> SetAutoTuningLevelAsync(AutoTuningLevel level, CancellationToken ct = default);

    /// <summary>指定アダプタの IPv4/IPv6 MTU を設定する (ジャンボフレーム有効化を含む)。</summary>
    Task<IReadOnlyList<CommandExecutionResult>> SetMtuAsync(string adapterId, int mtu, CancellationToken ct = default);

    /// <summary>指定アダプタの現在の IPv4 MTU を取得する。取得できない場合は null。</summary>
    Task<int?> GetMtuAsync(string adapterId, CancellationToken ct = default);
}

public enum TcpGlobalOption
{
    Rsc,
    EcnCapability,
    Timestamps,
    Rss,
    FastOpen,
}

/// <summary>
/// TCP 受信ウィンドウ自動調整 (auto-tuning) レベル。値は Windows の
/// <c>netsh interface tcp set global autotuninglevel=</c> / <c>Get-NetTCPSetting</c> の
/// AutoTuningLevelLocal と同じ 5 値 (既定は Normal)。
/// </summary>
public enum AutoTuningLevel
{
    Disabled,
    HighlyRestricted,
    Restricted,
    Normal,
    Experimental,
}
