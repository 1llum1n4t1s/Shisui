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
    public static CommandExecutionResult Skipped(string reason) =>
        new(false, reason, -1, string.Empty, reason);
}
