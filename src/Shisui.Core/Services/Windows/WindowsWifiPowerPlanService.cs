using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>現在の電源プランの Wi-Fi AC 設定を、元の値の保存と読み戻しを伴って変更する。</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsWifiPowerPlanService(ICommandExecutor executor, ISettingsService settingsService)
{
    private static readonly TimeSpan RollbackTimeout = TimeSpan.FromSeconds(30);

    internal async Task<CommandExecutionResult> RunAsync(bool restore, CancellationToken ct)
    {
        var initial = await ReadActiveStateAsync(ct);
        if (initial.Error is not null)
        {
            return initial.Error;
        }

        var before = initial.State!;
        var journal = settingsService.Current.WifiPowerPlanSnapshots ?? [];
        if (journal.Any(s => s is null || s.PlanGuid == Guid.Empty || s.OriginalAcValue is < 1 or > 3)
            || journal.Select(s => s.PlanGuid).Distinct().Count() != journal.Count)
        {
            return Failure("保存した Wi-Fi 復元情報が不正または重複しているため、設定を変更しませんでした。");
        }

        var snapshot = journal.SingleOrDefault(s => s.PlanGuid == before.PlanGuid);
        if (restore && snapshot is null)
        {
            return Failure("現在の電源プランには保存した Wi-Fi 設定がないため、復元しませんでした。");
        }

        if (restore && before.AcValue != 0 && before.AcValue != snapshot!.OriginalAcValue)
        {
            return Failure("Wi-Fi の AC 設定が適用時の最大パフォーマンスから変更されているため、復元しませんでした。");
        }

        if ((!restore && before.AcValue == 0) || (restore && before.AcValue == snapshot!.OriginalAcValue))
        {
            // すでに目標値なら書き込まず、現在のプランと DC 値を再確認する。
            var confirmed = await ReadActiveStateAsync(ct);
            if (confirmed.Error is not null)
            {
                return confirmed.Error;
            }
            if (confirmed.State != before)
            {
                return Failure("確認中に電源プランまたは Wi-Fi の AC/DC 設定が変わったため、処理を中止しました。復元情報は保持しています。");
            }

            if (restore)
            {
                try
                {
                    await RemoveRestoredSnapshotAsync(before.PlanGuid, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return Failure($"Wi-Fi は元の AC 値ですが、復元情報の保存に失敗しました。復元情報は保持しています: {ex.Message}");
                }
            }

            return Success(restore
                ? "Wi-Fi が保存した元の AC 値に戻っており、DC 設定が変更されていないことを確認しました。"
                : "Wi-Fi の AC 設定はすでに最大パフォーマンスで、DC 設定が変更されていないことを確認しました。");
        }

        if (!restore && snapshot is null)
        {
            snapshot = new WifiPowerPlanSnapshot { PlanGuid = before.PlanGuid, OriginalAcValue = before.AcValue };
            settingsService.Current.WifiPowerPlanSnapshots = [.. journal, snapshot];
            try
            {
                // 元の値を永続化できなければ OS への書き込みを始めない。
                await settingsService.SaveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                settingsService.Current.WifiPowerPlanSnapshots = journal;
                throw;
            }
            catch (Exception ex)
            {
                settingsService.Current.WifiPowerPlanSnapshots = journal;
                return Failure($"Wi-Fi の元の値を保存できなかったため、設定を変更しませんでした: {ex.Message}");
            }
        }

        // 保存中の外部変更も確認する。別プランや AC/DC の変化があれば書き込まない。
        var unchanged = await ReadActiveStateAsync(ct);
        if (unchanged.Error is not null)
        {
            return unchanged.Error;
        }

        if (unchanged.State != before)
        {
            return Failure("確認中に電源プランまたは Wi-Fi の AC/DC 設定が変わったため、設定を変更しませんでした。復元情報は保持しています。");
        }

        var targetAc = restore ? snapshot!.OriginalAcValue : 0;
        var writeAttempted = false;
        try
        {
            var activeError = await CheckActivePlanAsync(before.PlanGuid, ct);
            if (activeError is not null)
            {
                return activeError;
            }

            // 失敗・キャンセルでも、子プロセスが書き込んだ可能性があるので復旧を試す。
            writeAttempted = true;
            var setResult = await RunPowerCfgAsync(
                WindowsPowerPlanCommandBuilder.SetAcWirelessPowerSavingArguments(before.PlanGuid, targetAc), ct);
            if (!setResult.Success)
            {
                return await RecoverFailureAsync(before, targetAc, setResult);
            }

            activeError = await CheckActivePlanAsync(before.PlanGuid, ct);
            if (activeError is not null)
            {
                return await RecoverFailureAsync(before, targetAc, activeError);
            }

            var activateResult = await RunPowerCfgAsync(WindowsPowerPlanCommandBuilder.ActivateSchemeArguments(before.PlanGuid), ct);
            if (!activateResult.Success)
            {
                return await RecoverFailureAsync(before, targetAc, activateResult);
            }

            var after = await ReadActiveStateAsync(ct);
            if (after.Error is not null)
            {
                return await RecoverFailureAsync(before, targetAc, after.Error);
            }

            if (after.State!.PlanGuid != before.PlanGuid || after.State.AcValue != targetAc || after.State.DcValue != before.DcValue)
            {
                return await RecoverFailureAsync(before, targetAc, Failure("適用後の電源プラン・Wi-Fi AC 値・DC 値を確認できませんでした。"));
            }

            if (restore)
            {
                await RemoveRestoredSnapshotAsync(before.PlanGuid, ct);
            }

            return Success(restore
                    ? "Wi-Fi の AC 設定を保存した元の値へ復元し、DC 設定が変更されていないことを確認しました。"
                    : "Wi-Fi の AC 設定を最大パフォーマンスにし、DC 設定が変更されていないことを確認しました。");
        }
        catch (OperationCanceledException)
        {
            if (writeAttempted)
            {
                await TryRollbackAsync(before, targetAc);
            }
            throw;
        }
        catch (Exception ex)
        {
            var error = Failure($"Wi-Fi の電源設定を完了できませんでした: {ex.Message}");
            return writeAttempted ? await RecoverFailureAsync(before, targetAc, error) : error;
        }
    }

    private async Task RemoveRestoredSnapshotAsync(Guid planGuid, CancellationToken ct)
    {
        var savedJournal = settingsService.Current.WifiPowerPlanSnapshots ?? [];
        settingsService.Current.WifiPowerPlanSnapshots = savedJournal.Where(s => s.PlanGuid != planGuid).ToList();
        try
        {
            await settingsService.SaveAsync(ct);
        }
        catch
        {
            // 保存完了前に削除を確定せず、失敗時は復元情報を戻す。
            settingsService.Current.WifiPowerPlanSnapshots = savedJournal;
            throw;
        }
    }

    private async Task<(PowerState? State, CommandExecutionResult? Error)> ReadActiveStateAsync(CancellationToken ct)
    {
        var active = await RunPowerCfgAsync(WindowsPowerPlanCommandBuilder.GetActiveSchemeArguments, ct);
        if (!active.Success)
        {
            return (null, active);
        }
        if (!WindowsPowerPlanCommandBuilder.TryParseActiveScheme(active.StandardOutput, out var planGuid) || planGuid == Guid.Empty)
        {
            return (null, Failure("現在の電源プランを一意に確認できないため、設定を変更しませんでした。", active.StandardOutput));
        }

        var query = await RunPowerCfgAsync(WindowsPowerPlanCommandBuilder.QueryWirelessPowerSavingArguments(planGuid), ct);
        if (!query.Success)
        {
            return (null, Failure("Wi-Fi の AC/DC 設定を取得できませんでした。この PC の電源プランで非対応の場合は変更できません。", query.StandardOutput, query.StandardError));
        }
        if (!WindowsPowerPlanCommandBuilder.TryParseWirelessPowerSavingValues(query.StandardOutput, planGuid, out var ac, out var dc))
        {
            return (null, Failure("Wi-Fi の AC/DC 設定を一意に確認できないため、設定を変更しませんでした。", query.StandardOutput));
        }

        var activeError = await CheckActivePlanAsync(planGuid, ct);
        return activeError is null ? (new PowerState(planGuid, ac, dc), null) : (null, activeError);
    }

    private async Task<CommandExecutionResult?> CheckActivePlanAsync(Guid planGuid, CancellationToken ct)
    {
        var active = await RunPowerCfgAsync(WindowsPowerPlanCommandBuilder.GetActiveSchemeArguments, ct);
        return !active.Success ? active
            : !WindowsPowerPlanCommandBuilder.TryParseActiveScheme(active.StandardOutput, out var currentGuid) || currentGuid != planGuid
                ? Failure("確認中に現在の電源プランが変わったため、処理を中止しました。別のプランへの切り替えは行いません。", active.StandardOutput)
                : null;
    }

    private async Task<CommandExecutionResult> RecoverFailureAsync(PowerState before, uint expectedWrittenAc, CommandExecutionResult error)
    {
        var recovered = await TryRollbackAsync(before, expectedWrittenAc);
        return Failure(error.StandardError + (recovered
            ? " 変更前の AC 値であることを確認しました。復元情報は保持しています。"
            : " 変更前の AC 値への復旧を確認できませんでした。復元情報は保持しています。"), error.StandardOutput);
    }

    private async Task<bool> TryRollbackAsync(PowerState before, uint expectedWrittenAc)
    {
        try
        {
            // 元のキャンセルから独立して復旧を試し、全コマンドを合わせた待機時間を制限する。
            using var deadline = new CancellationTokenSource(RollbackTimeout);
            // 今回書いた値以外へ変わっている場合は、外部の変更を上書きしない。
            var current = await RunPowerCfgAsync(WindowsPowerPlanCommandBuilder.QueryWirelessPowerSavingArguments(before.PlanGuid), deadline.Token);
            if (!current.Success
                || !WindowsPowerPlanCommandBuilder.TryParseWirelessPowerSavingValues(current.StandardOutput, before.PlanGuid, out var currentAc, out var currentDc))
            {
                return false;
            }
            if (currentAc == before.AcValue)
            {
                return currentDc == before.DcValue;
            }
            if (currentAc != expectedWrittenAc)
            {
                return false;
            }

            var result = await RunPowerCfgAsync(
                WindowsPowerPlanCommandBuilder.SetAcWirelessPowerSavingArguments(before.PlanGuid, before.AcValue), deadline.Token);
            if (!result.Success)
            {
                return false;
            }

            // 別のプランが選択済みならその選択を尊重し、元のプランを再選択しない。
            var active = await RunPowerCfgAsync(WindowsPowerPlanCommandBuilder.GetActiveSchemeArguments, deadline.Token);
            if (!active.Success || !WindowsPowerPlanCommandBuilder.TryParseActiveScheme(active.StandardOutput, out var currentGuid))
            {
                return false;
            }
            if (currentGuid == before.PlanGuid
                && !(await RunPowerCfgAsync(WindowsPowerPlanCommandBuilder.ActivateSchemeArguments(before.PlanGuid), deadline.Token)).Success)
            {
                return false;
            }

            var query = await RunPowerCfgAsync(WindowsPowerPlanCommandBuilder.QueryWirelessPowerSavingArguments(before.PlanGuid), deadline.Token);
            return query.Success
                && WindowsPowerPlanCommandBuilder.TryParseWirelessPowerSavingValues(query.StandardOutput, before.PlanGuid, out var ac, out var dc)
                && ac == before.AcValue && dc == before.DcValue;
        }
        catch
        {
            // 復旧失敗で元の例外・キャンセルを隠さず、永続化済みの復元情報も消さない。
            return false;
        }
    }

    private Task<CommandExecutionResult> RunPowerCfgAsync(string arguments, CancellationToken ct) =>
        executor.RunAsync(Path.Combine(Environment.SystemDirectory, "powercfg.exe"), arguments, ct);

    private static CommandExecutionResult Success(string output) =>
        new(true, "powercfg Wi-Fi 省電力設定 (AC のみ)", 0, output, string.Empty);

    private static CommandExecutionResult Failure(string message, string output = "", string error = "") =>
        new(false, "powercfg Wi-Fi 省電力設定 (AC のみ)", -1, output, message + (error.Length == 0 ? "" : " " + error));

    private sealed record PowerState(Guid PlanGuid, uint AcValue, uint DcValue);
}
