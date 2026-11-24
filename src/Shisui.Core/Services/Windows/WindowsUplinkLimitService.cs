using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// ActiveStore の QoS ポリシー (ShisuiUplinkLimit / ShisuiUplinkTrial) を操作する。
/// 他のポリシーがある間は書き込まず、書き込み・削除は読み戻しで確認する。変更ゲートは呼び出し側が持つ。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUplinkLimitService(ICommandExecutor executor) : IUplinkLimitService
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(30);

    /// <summary>読み取り専用。単発の読み取り失敗で計測の中止や再適用の見送りを起こさないよう、1 回だけ読み直す。</summary>
    public async Task<UplinkLimitState> GetStateAsync(CancellationToken ct = default)
    {
        var state = (await QueryAsync(ct)).State;
        return state.IsReadable || ct.IsCancellationRequested ? state : (await QueryAsync(ct)).State;
    }

    public Task<UplinkLimitOperationResult> ApplyLimitAsync(long bitsPerSecond, CancellationToken ct = default) =>
        ApplyAsync(UplinkLimitPolicy.LimitPolicyName, bitsPerSecond, ct);

    public Task<UplinkLimitOperationResult> ApplyTrialAsync(long bitsPerSecond, CancellationToken ct = default) =>
        ApplyAsync(UplinkLimitPolicy.TrialPolicyName, bitsPerSecond, ct);

    public Task<UplinkLimitOperationResult> RemoveLimitAsync(CancellationToken ct = default) =>
        RemoveAsync(UplinkLimitPolicy.LimitPolicyName, ct);

    public Task<UplinkLimitOperationResult> RemoveTrialAsync(CancellationToken ct = default) =>
        RemoveAsync(UplinkLimitPolicy.TrialPolicyName, ct);

    public async Task<UplinkLimitOperationResult> BeginMeasurementAsync(CancellationToken ct = default)
    {
        var (state, query) = await QueryAsync(ct);
        var rejection = Reject(state, [query]);
        if (rejection is not null) return rejection;

        var commands = new List<CommandExecutionResult> { query };
        long? previous = state.LimitPolicy is { IsDefaultTemplate: true } limit && UplinkLimitPolicy.IsValidRate(limit.ThrottleBitsPerSecond)
            ? limit.ThrottleBitsPerSecond : null;
        foreach (var name in new[] { UplinkLimitPolicy.TrialPolicyName, UplinkLimitPolicy.LimitPolicyName })
        {
            var exists = name == UplinkLimitPolicy.TrialPolicyName ? state.TrialPolicy is not null : state.LimitPolicy is not null;
            if (!exists) continue;
            var removed = await RemoveAsync(name, ct);
            commands.AddRange(removed.Commands);
            if (!removed.Success) return removed with { Commands = commands };
        }

        return new(UplinkOperationOutcome.Succeeded, "計測の準備として既存の Shisui の上限ポリシーを外しました。", commands, [], previous);
    }

    public async Task<UplinkLimitOperationResult> EndMeasurementAsync(long? restoreLimitBitsPerSecond, CancellationToken ct = default)
    {
        var removed = await RemoveAsync(UplinkLimitPolicy.TrialPolicyName, ct);
        if (!removed.Success || restoreLimitBitsPerSecond is null) return removed;

        var restored = await ApplyAsync(UplinkLimitPolicy.LimitPolicyName, restoreLimitBitsPerSecond.Value, ct);
        return restored with { Commands = [.. removed.Commands, .. restored.Commands] };
    }

    private async Task<UplinkLimitOperationResult> ApplyAsync(string name, long bitsPerSecond, CancellationToken ct)
    {
        if (!UplinkLimitPolicy.IsValidRate(bitsPerSecond))
            return new(UplinkOperationOutcome.RejectedOutOfRange,
                $"上限は {UplinkLimitPolicy.MinBitsPerSecond / 1_000_000} Mbps から {UplinkLimitPolicy.MaxBitsPerSecond / 1_000_000_000} Gbps の範囲で指定してください。", [], []);

        var (state, query) = await QueryAsync(ct);
        var rejection = Reject(state, [query]);
        if (rejection is not null) return rejection;

        var isLimit = name == UplinkLimitPolicy.LimitPolicyName;
        if ((isLimit ? state.TrialPolicy : state.LimitPolicy) is not null)
            return new(UplinkOperationOutcome.RejectedConflict,
                "試験用と適用用の上限は同時に存在させられません。計測の準備または終了の操作を先に行ってください。", [query], []);

        var apply = await ExecuteAsync(WindowsUplinkLimitCommandBuilder.BuildApplyArguments(name, bitsPerSecond), "上り速度制限: 適用", true, ct);
        var commands = new[] { query, apply };
        if (!apply.Success || apply.ExitCode != 0 || apply.RequiresOperationAbort)
            return new(UplinkOperationOutcome.Failed, "上限ポリシーの作成に失敗しました。状態を再確認してください。", commands, []);

        var readback = WindowsUplinkLimitCommandBuilder.ParsePolicyReadback(apply.StandardOutput);
        if (readback is [var policy] && policy.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && policy.ThrottleBitsPerSecond == bitsPerSecond && policy.IsDefaultTemplate)
            return new(UplinkOperationOutcome.Succeeded, "上限ポリシーを作成し、読み戻しで一致を確認しました。", commands, []);

        // 期待と異なる状態を残さないよう、読み戻し不一致の自分のポリシーは削除を試みる。
        var rollback = await RemoveAsync(name, CancellationToken.None);
        return new(UplinkOperationOutcome.VerificationFailed,
            rollback.Success ? "上限ポリシーの読み戻しが一致しなかったため、削除しました。" : "上限ポリシーの読み戻しが一致せず、削除も確認できませんでした。",
            [.. commands, .. rollback.Commands], []);
    }

    private async Task<UplinkLimitOperationResult> RemoveAsync(string name, CancellationToken ct)
    {
        var remove = await ExecuteAsync(WindowsUplinkLimitCommandBuilder.BuildRemoveArguments(name), "上り速度制限: 削除", true, ct);
        if (!remove.Success || remove.ExitCode != 0 || remove.RequiresOperationAbort)
            return new(UplinkOperationOutcome.Failed, "上限ポリシーの削除に失敗しました。状態を再確認してください。", [remove], []);

        return WindowsUplinkLimitCommandBuilder.ParsePolicyReadback(remove.StandardOutput) is { Count: 0 }
            ? new(UplinkOperationOutcome.Succeeded, "上限ポリシーが 0 件であることを読み戻しで確認しました。", [remove], [])
            : new(UplinkOperationOutcome.VerificationFailed, "削除後の読み戻しで上限ポリシーが残っているか、確認できませんでした。", [remove], []);
    }

    private async Task<(UplinkLimitState State, CommandExecutionResult Command)> QueryAsync(CancellationToken ct)
    {
        var result = await ExecuteAsync(WindowsUplinkLimitCommandBuilder.BuildQueryArguments(), "上り速度制限: 状態取得", false, ct);
        if (!result.Success || result.ExitCode != 0)
            return (UplinkLimitState.Unreadable("QoS ポリシーを読み取れませんでした (管理者権限が必要です)。"), result);
        return (WindowsUplinkLimitCommandBuilder.ParseState(result.StandardOutput), result);
    }

    private static UplinkLimitOperationResult? Reject(UplinkLimitState state, IReadOnlyList<CommandExecutionResult> commands)
    {
        if (!state.IsReadable)
            return new(UplinkOperationOutcome.StateUnreadable, state.Detail ?? "QoS ポリシーの状態を読み取れませんでした。", commands, []);
        var others = state.OtherPolicyNames;
        return others.Count == 0 ? null
            : new(UplinkOperationOutcome.RejectedOtherPolicies,
                "Shisui 以外の QoS ポリシーがあるため操作しません: " + string.Join(", ", others), commands, others);
    }

    private async Task<CommandExecutionResult> ExecuteAsync(string arguments, string label, bool mutating, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(mutating ? WriteTimeout : ReadTimeout);
        var timeoutText = $"{label} がタイムアウトしました。";
        try
        {
            var result = await executor.RunAsync(FileName(), arguments, deadline.Token);
            if (!deadline.IsCancellationRequested)
                return mutating && result.ExitCode < 0 ? result with { RequiresOperationAbort = true } : result;
            return result with
            {
                Success = false,
                RequiresOperationAbort = mutating,
                StandardError = result.StandardError + Environment.NewLine + (ct.IsCancellationRequested ? "処理がキャンセルされました。" : timeoutText),
            };
        }
        catch (OperationCanceledException)
        {
            return Failure(label, ct.IsCancellationRequested ? "処理がキャンセルされました。" : timeoutText, mutating);
        }
        catch (Exception ex)
        {
            return Failure(label, $"コマンドの実行に失敗しました: {ex.Message}", mutating);
        }
    }

    private static string FileName() => WindowsUplinkLimitCommandBuilder.FileName;

    private static CommandExecutionResult Failure(string label, string error, bool abort) =>
        new(false, label, -1, string.Empty, error) { RequiresOperationAbort = abort };
}
