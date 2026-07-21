namespace Shisui.Core.Models;

/// <summary>切断済み NIC の接続名予約削除と、現在の接続名変更の実行結果。</summary>
public sealed record NetworkAdapterNameCleanupResult(
    bool Success,
    string? OriginalName,
    string? TargetName,
    int RemovedGhostCount,
    int FailedGhostCount,
    bool WasRenamed,
    IReadOnlyList<CommandExecutionResult> CommandResults,
    string? ErrorMessage);
