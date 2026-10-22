using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>全 NIC の原本を永続化してから初期化し、終了確認と読み戻しができるまで原本を保護する。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsIpStackResetService(IIpStackPlatform platform, ISettingsService settingsService)
    : IIpStackResetService
{
    private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan JournalTimeout = TimeSpan.FromSeconds(15);

    public bool IsRecoveryRequired => settingsService.Current.PendingIpStackReset is { } journal
        && (journal.RequiresRebootBeforeRecovery || journal.Phase is not ("Restored" or "AwaitingReboot"));

    public async Task<IReadOnlyList<CommandExecutionResult>> ResetAndRestoreAsync(CancellationToken ct = default)
    {
        var results = new List<CommandExecutionResult>();
        if (settingsService.Current.PendingIpStackReset is not null)
        {
            results.AddRange(await RecoverPendingAsync(ct));
            if (IsRecoveryRequired || results.Any(r => r.RequiresOperationAbort))
            {
                return results;
            }

            // 同じ journal が残る間は、原本を差し替えず reset も再発行しない。
            if (settingsService.Current.PendingIpStackReset is not null)
            {
                results.Add(Success("退避済みの IP 初期化情報を保持し、初期化の再実行を省略しました。"));
                return results;
            }
        }

        IpStackResetJournal journal;
        try
        {
            var original = await platform.CaptureAsync(ct);
            ValidateSnapshot(original);
            journal = new IpStackResetJournal { Original = Clone(original) };
        }
        catch (Exception ex)
        {
            results.Add(Failure($"全 NIC の設定を完全に退避できないため、IP 初期化を中断しました: {ex.Message}"));
            return results;
        }

        if (!await SaveAsync(journal, results, ct))
        {
            return results;
        }

        try
        {
            var current = await platform.CaptureAsync(ct);
            ValidateSnapshot(current);
            if (!platform.Matches(journal.Original, current))
            {
                throw new InvalidOperationException("退避後に NIC または IP 設定が変更されました。");
            }
            ct.ThrowIfCancellationRequested();
        }
        catch (Exception ex)
        {
            results.Add(Failure($"IP 初期化前の再確認に失敗しました。OS 設定は変更していません: {ex.Message}"));
            await ClearPreparedAsync(results);
            return results;
        }

        // 強制終了して子だけが残った場合も、次回起動で同一 boot 内の復旧を競合させない。
        journal = WithPhase(journal, "ResetStarted", requiresReboot: true);
        journal.LastMutationBootId = journal.Original.BootId;
        if (!await SaveAsync(journal, results, ct))
        {
            return results;
        }

        var attemptedReset = false;
        foreach (var resetIpv4 in new[] { true, false })
        {
            if (ct.IsCancellationRequested)
            {
                results.Add(Failure("IP 初期化を取り消しました。終了済みの処理を元に戻します。"));
                break;
            }

            CommandExecutionResult result;
            try
            {
                attemptedReset = true;
                result = resetIpv4
                    ? await platform.ResetIpv4Async(ct)
                    : await platform.ResetIpv6Async(ct);
            }
            catch (Exception ex)
            {
                await MarkUncertainAsync(journal, $"IP 初期化の終了を確認できません: {ex.Message}", results);
                return results;
            }

            results.Add(result);
            if (result.RequiresOperationAbort)
            {
                await MarkUncertainAsync(journal, "IP 初期化の子プロセスの終了を確認できません。再起動後に復旧してください。", results);
                return results;
            }
            // 非 0 終了でも一部だけ初期化された可能性があるため、必ず復元を試みる。
        }

        if (!attemptedReset)
        {
            await ClearPreparedAsync(results);
            return results;
        }
        if (ct.IsCancellationRequested && !results.Any(r => r.RequiresOperationAbort))
        {
            results.Add(Failure("IP 初期化を取り消しました。終了済みの処理を元に戻します。"));
        }
        await RestoreAsync(journal, results);
        return results;
    }

    public async Task<IReadOnlyList<CommandExecutionResult>> RecoverPendingAsync(CancellationToken ct = default)
    {
        var results = new List<CommandExecutionResult>();
        var journal = settingsService.Current.PendingIpStackReset;
        if (journal is null)
        {
            return results;
        }
        if (!TryValidateJournal(journal, results))
        {
            return results;
        }
        if (journal.Phase == "Prepared")
        {
            await ClearPreparedAsync(results);
            return results;
        }

        string bootId;
        try
        {
            bootId = await platform.GetBootIdAsync(ct);
            if (string.IsNullOrWhiteSpace(bootId))
            {
                throw new InvalidOperationException("起動識別子を取得できませんでした。");
            }
        }
        catch (Exception ex)
        {
            results.Add(Failure($"IP 復旧前の再起動確認に失敗しました: {ex.Message}"));
            return results;
        }

        if (journal.RequiresRebootBeforeRecovery && bootId == (journal.LastMutationBootId ?? journal.Original.BootId))
        {
            results.Add(Failure("前回の IP 変更処理の終了が未確認です。競合を避けるため、Windows 再起動後に復旧してください。"));
            return results;
        }

        if (journal.Phase == "Restored")
        {
            // DNS/TCP/NIC 最適化が途中まで進んでいる可能性があり、原本を自動再適用しない。
            results.Add(Success("IP 設定の復元は完了しています。最適化後の確認まで退避情報を保持します。"));
            return results;
        }
        if (journal.Phase == "AwaitingReboot")
        {
            if (bootId == journal.ExpectedFinal!.BootId)
            {
                results.Add(Success("再起動後の IP 設定確認を待っています。退避情報を保持します。"));
                return results;
            }
            try
            {
                var current = await platform.CaptureAsync(ct);
                ValidateSnapshot(current);
                if (!platform.Matches(journal.ExpectedFinal!, current, afterReboot: true))
                {
                    await RecordErrorAsync(journal,
                        "再起動後の永続 IP 設定が最適化完了時の設定と一致しません。退避情報を保持しています。必要なら元の設定を明示的に復元してください。", results);
                    return results;
                }
                if (await SaveAsync(null, results, ct))
                {
                    results.Add(Success("再起動後の永続 IP 設定を確認し、退避情報を削除しました。"));
                }
            }
            catch (Exception ex)
            {
                await RecordErrorAsync(journal, $"再起動後の IP 設定を確認できません。退避情報を保持しています: {ex.Message}", results);
            }
            return results;
        }

        await RestoreAsync(journal, results);
        return results;
    }

    public async Task<IReadOnlyList<CommandExecutionResult>> CompleteOptimizationAsync(CancellationToken ct = default)
    {
        var results = new List<CommandExecutionResult>();
        var journal = settingsService.Current.PendingIpStackReset;
        if (journal is null || !TryValidateJournal(journal, results)
            || journal.Phase is not ("Restored" or "AwaitingReboot") || journal.RequiresRebootBeforeRecovery)
        {
            if (results.Count == 0)
            {
                results.Add(Failure("IP 設定の復元完了を確認できないため、ルーティングテーブルの初期化を中断しました。"));
            }
            return results;
        }

        try
        {
            var expected = await platform.CaptureAsync(ct);
            ValidateSnapshot(expected);
            var next = WithPhase(journal, "AwaitingReboot");
            next.ExpectedFinal = Clone(expected);
            if (await SaveAsync(next, results, ct))
            {
                results.Add(Success("最適化後の IP 設定を保存しました。再起動後に永続設定を照合します。"));
            }
        }
        catch (Exception ex)
        {
            results.Add(Failure($"最適化後の IP 設定を保存できないため、ルーティングテーブルの初期化を中断しました: {ex.Message}"));
        }
        return results;
    }

    public async Task<IReadOnlyList<CommandExecutionResult>> RestoreOriginalAsync(CancellationToken ct = default)
    {
        var results = new List<CommandExecutionResult>();
        var journal = settingsService.Current.PendingIpStackReset;
        if (journal is null)
        {
            results.Add(Success("復元する IP 設定の退避情報はありません。"));
            return results;
        }
        if (!TryValidateJournal(journal, results))
        {
            return results;
        }
        try
        {
            ct.ThrowIfCancellationRequested();
            var bootId = await platform.GetBootIdAsync(ct);
            if (string.IsNullOrWhiteSpace(bootId))
            {
                throw new InvalidOperationException("起動識別子を取得できませんでした。");
            }
            if (journal.RequiresRebootBeforeRecovery
                && bootId == (journal.LastMutationBootId ?? journal.Original.BootId))
            {
                results.Add(Failure("前回の IP 変更処理の終了が未確認です。Windows 再起動後に元の設定を復元してください。"));
                return results;
            }
        }
        catch (Exception ex)
        {
            results.Add(Failure($"元の IP 設定の復元を開始できません: {ex.Message}"));
            return results;
        }

        if (await RestoreAsync(journal, results))
        {
            using var deadline = new CancellationTokenSource(JournalTimeout);
            if (await SaveAsync(null, results, deadline.Token))
            {
                results.Add(Success("元の IP 設定を読み戻し確認し、退避情報を削除しました。"));
            }
        }
        return results;
    }

    private async Task<bool> RestoreAsync(IpStackResetJournal journal, List<CommandExecutionResult> results)
    {
        using var deadline = new CancellationTokenSource(RecoveryTimeout);
        var restoring = WithPhase(journal, "RestoreStarted", requiresReboot: true);
        try
        {
            restoring.LastMutationBootId = await platform.GetBootIdAsync(deadline.Token);
            if (string.IsNullOrWhiteSpace(restoring.LastMutationBootId))
            {
                throw new InvalidOperationException("起動識別子を取得できませんでした。");
            }
        }
        catch (Exception ex)
        {
            results.Add(Failure($"IP 設定の復元前に再起動状態を確認できません: {ex.Message}"));
            return false;
        }
        if (!await SaveAsync(restoring, results, deadline.Token))
        {
            return false;
        }

        CommandExecutionResult restored;
        try
        {
            restored = await platform.RestoreAsync(Clone(journal.Original), deadline.Token);
        }
        catch (Exception ex)
        {
            await MarkUncertainAsync(restoring, $"IP 設定の復元処理の終了を確認できません: {ex.Message}", results);
            return false;
        }
        results.Add(restored);
        if (restored.RequiresOperationAbort)
        {
            await MarkUncertainAsync(restoring, "IP 設定の復元処理の終了が未確認です。Windows 再起動後に復旧してください。", results);
            return false;
        }
        if (!restored.Success)
        {
            await RecordErrorAsync(WithPhase(restoring, "RestoreStarted"),
                "IP 設定の復元処理が失敗しました。復元情報を保持し、後続の最適化を中断しました。", results);
            return false;
        }

        try
        {
            var current = await platform.CaptureAsync(deadline.Token);
            ValidateSnapshot(current);
            // 再起動後の復旧でも復元直後は active-only を含む全保存値を確認する。
            if (!platform.Matches(journal.Original, current))
            {
                await RecordErrorAsync(WithPhase(restoring, "RestoreStarted"),
                    "元の IP 設定との一致を確認できませんでした。復元情報を保持し、後続の最適化を中断しました。", results);
                return false;
            }

            if (!await SaveAsync(WithPhase(restoring, "Restored"), results, deadline.Token))
            {
                return false;
            }
            results.Add(Success("全 NIC の元の IP 設定を復元し、読み戻し確認と復元完了の保存に成功しました。"));
            return true;
        }
        catch (Exception ex)
        {
            // 復元プロセスは終了確認済み。読み取りの失敗では再起動を必須にしない。
            await RecordErrorAsync(WithPhase(restoring, "RestoreStarted"),
                $"IP 設定の復元後確認に失敗しました。復元情報を保持しています: {ex.Message}", results);
            return false;
        }
    }

    private async Task ClearPreparedAsync(List<CommandExecutionResult> results)
    {
        using var deadline = new CancellationTokenSource(JournalTimeout);
        if (await SaveAsync(null, results, deadline.Token))
        {
            results.Add(Success("IP 初期化前に中止した退避情報を削除しました。OS 設定は変更していません。"));
        }
    }

    private Task MarkUncertainAsync(IpStackResetJournal journal, string error, List<CommandExecutionResult> results) =>
        RecordErrorAsync(WithPhase(journal, journal.Phase, requiresReboot: true), error, results);

    private async Task RecordErrorAsync(IpStackResetJournal journal, string error, List<CommandExecutionResult> results)
    {
        var next = WithPhase(journal, journal.Phase, journal.RequiresRebootBeforeRecovery);
        next.LastError = error;
        results.Add(Failure(error));
        using var deadline = new CancellationTokenSource(JournalTimeout);
        await SaveAsync(next, results, deadline.Token);
    }

    private async Task<bool> SaveAsync(IpStackResetJournal? next, List<CommandExecutionResult> results, CancellationToken ct)
    {
        var previous = settingsService.Current.PendingIpStackReset;
        settingsService.Current.PendingIpStackReset = next;
        try
        {
            await settingsService.SaveAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            // 保存対象は既存インスタンスを変更せず差し替える。失敗時は disk と同じ原本へ戻す。
            settingsService.Current.PendingIpStackReset = previous;
            results.Add(Failure($"IP 初期化の復元情報を保存できません。後続処理を中断しました: {ex.Message}"));
            return false;
        }
    }

    private bool TryValidateJournal(IpStackResetJournal journal, List<CommandExecutionResult> results)
    {
        try
        {
            if (journal.SchemaVersion != 1 || !Guid.TryParse(journal.OperationId, out _)
                || journal.Phase is not ("Prepared" or "ResetStarted" or "RestoreStarted" or "Restored" or "AwaitingReboot")
                || journal.LastMutationBootId is { } lastBoot && string.IsNullOrWhiteSpace(lastBoot))
            {
                throw new InvalidOperationException("未対応または不正な復元情報です。");
            }
            ValidateSnapshot(journal.Original);
            if (journal.Phase == "AwaitingReboot")
            {
                ValidateSnapshot(journal.ExpectedFinal!);
            }
            return true;
        }
        catch (Exception ex)
        {
            results.Add(Failure($"保存済みの IP 復元情報を検証できません。原本を保持しています: {ex.Message}"));
            return false;
        }
    }

    private void ValidateSnapshot(IpStackSnapshot snapshot)
    {
        if (snapshot is null || snapshot.SchemaVersion != 1 || string.IsNullOrWhiteSpace(snapshot.BootId)
            || snapshot.CapturedAtUtc == default || !platform.Matches(snapshot, snapshot))
        {
            throw new InvalidOperationException("退避した IP 設定に不足または未対応の値があります。");
        }
    }

    private static IpStackResetJournal WithPhase(IpStackResetJournal journal, string phase, bool requiresReboot = false) => new()
    {
        SchemaVersion = journal.SchemaVersion,
        OperationId = journal.OperationId,
        Phase = phase,
        Original = journal.Original,
        ExpectedFinal = journal.ExpectedFinal,
        RequiresRebootBeforeRecovery = requiresReboot,
        LastMutationBootId = journal.LastMutationBootId,
        LastError = null
    };

    private static CommandExecutionResult Success(string message) => new(true, "IP スタック初期化・復元", 0, message, string.Empty);
    private static CommandExecutionResult Failure(string message) => new(false, "IP スタック初期化・復元", -1, string.Empty, message)
    { RequiresOperationAbort = true };

    private static IpStackSnapshot Clone(IpStackSnapshot value) => new()
    {
        SchemaVersion = value.SchemaVersion,
        BootId = value.BootId,
        CapturedAtUtc = value.CapturedAtUtc,
        Adapters = value.Adapters.Select(a => new IpStackAdapterSnapshot
        {
            AdapterId = a.AdapterId, InterfaceIndex = a.InterfaceIndex, Description = a.Description,
            PnpDeviceId = a.PnpDeviceId, HasNetworkConfiguration = a.HasNetworkConfiguration,
            NetbiosOptions = a.NetbiosOptions, StaticWinsServers = [.. a.StaticWinsServers]
        }).ToList(),
        Interfaces = value.Interfaces.Select(i => new IpStackInterfaceSnapshot
        {
            AdapterId = i.AdapterId, AddressFamily = i.AddressFamily, PolicyStore = i.PolicyStore,
            Settings = CloneSettings(i.Settings)
        }).ToList(),
        Addresses = value.Addresses.Select(a => new IpStackAddressSnapshot
        {
            AdapterId = a.AdapterId, AddressFamily = a.AddressFamily, PolicyStore = a.PolicyStore,
            Address = a.Address, PrefixLength = a.PrefixLength, SkipAsSource = a.SkipAsSource, Type = a.Type
        }).ToList(),
        Routes = value.Routes.Select(r => new IpStackRouteSnapshot
        {
            AdapterId = r.AdapterId, AddressFamily = r.AddressFamily, PolicyStore = r.PolicyStore,
            DestinationPrefix = r.DestinationPrefix, NextHop = r.NextHop, RouteMetric = r.RouteMetric,
            Publish = r.Publish, Protocol = r.Protocol
        }).ToList(),
        Dns = value.Dns.Select(d => new IpStackDnsSnapshot
        {
            AdapterId = d.AdapterId, AddressFamily = d.AddressFamily, Automatic = d.Automatic,
            AutomaticSuffix = d.AutomaticSuffix,
            ServerAddresses = [.. d.ServerAddresses], ConnectionSpecificSuffix = d.ConnectionSpecificSuffix,
            RegisterThisConnectionsAddress = d.RegisterThisConnectionsAddress,
            UseSuffixWhenRegistering = d.UseSuffixWhenRegistering
        }).ToList()
    };

    private static List<IpStackSetting> CloneSettings(IEnumerable<IpStackSetting> settings) =>
        settings.Select(s => new IpStackSetting { Name = s.Name, Value = s.Value, IsTimeSpan = s.IsTimeSpan }).ToList();
}
