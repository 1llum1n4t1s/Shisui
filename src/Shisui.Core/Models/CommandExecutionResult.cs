namespace Shisui.Core.Models;

/// <summary>
/// 外部コマンド 1 回分の実行結果。UI のログパネルにそのまま表示する。
/// </summary>
public sealed record CommandExecutionResult(
    bool Success,
    string CommandLine,
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    /// <summary>ファイルログの開始・終了・画面通知を結ぶID。画面表示には使わない。</summary>
    public string? DiagnosticId { get; init; }

    public static CommandExecutionResult Skipped(string reason) =>
        new(false, reason, -1, string.Empty, reason);
}
