using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// 物理 Ethernet / 対応 Wi-Fi NIC の限定プロパティだけを変更し、元の値を設定へ永続化して復元可能にする。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsGamingNetworkProfileService(
    ICommandExecutor executor,
    ISettingsService settingsService,
    INetworkMutationGate mutationGate) : IGamingNetworkProfileService
{
    private const string TargetValue = "0";

    public async Task<IReadOnlyList<CommandExecutionResult>> ApplyAsync(
        string adapterName,
        CancellationToken ct = default)
    {
        using var lease = await mutationGate.EnterAsync(ct);
        var results = new List<CommandExecutionResult>();
        var current = await QueryAsync(adapterName, "ゲーム向け NIC 設定: 適用前確認", results, ct);
        if (current is null)
        {
            return results;
        }

        if (!IsSupportedPhysicalAdapter(current))
        {
            results.Add(Failure(
                "ゲーム向け NIC 設定: 対象外",
                "物理 Ethernet (InterfaceType=6) または物理 Wi-Fi (InterfaceType=71) だけが対象です。仮想 NIC は変更していません。"));
            return results;
        }

        var applicableKeywords = WindowsGamingNetworkProfilePolicy.GetApplicableKeywords(
            current.HardwareInterface,
            current.InterfaceType,
            current.DriverProvider,
            current.PnpDeviceId);
        var missing = applicableKeywords
            .Except(current.Properties.Select(property => property.RegistryKeyword), StringComparer.Ordinal)
            .ToArray();
        var changes = current.Properties
            .Where(property => property.CurrentValue != TargetValue)
            .ToArray();

        if (current.Properties.Count == 0)
        {
            results.Add(Failure(
                "ゲーム向け NIC 設定: 非対応",
                $"この NIC では安全に確認できる対象プロパティ ({string.Join(" / ", applicableKeywords)}) が見つかりません。" +
                "未知の機種を推測せず、変更していません。今回の操作による PC 再起動も不要です。"));
            return results;
        }

        var journals = EnsureJournalList();
        if (ContainsNullJournalData(journals))
        {
            results.Add(Failure("ゲーム向け NIC 設定: 復元情報確認", "保存済みの復元情報に null 項目があります。変更していません。"));
            return results;
        }

        var matches = journals.Where(snapshot => snapshot.AdapterGuid == current.InterfaceGuid).ToArray();
        if (matches.Length > 1)
        {
            results.Add(Failure("ゲーム向け NIC 設定: 復元情報確認", "同じ NIC GUID の復元情報が重複しています。変更していません。"));
            return results;
        }

        var journal = matches.SingleOrDefault();
        if (journal is not null && !TryValidateJournal(journal, current, out var journalError))
        {
            results.Add(Failure("ゲーム向け NIC 設定: 復元情報確認", journalError));
            return results;
        }

        if (changes.Length == 0)
        {
            results.Add(Success(
                "ゲーム向け NIC 設定: 変更不要",
                BuildCompletionMessage("対象プロパティは既に 0 です。設定値を確認済みです。", current, missing)));
            return results;
        }

        var beforeSave = CloneJournals(journals);
        var journalChanged = false;
        if (journal is null)
        {
            journal = new GamingNetworkProfileSnapshot
            {
                AdapterGuid = current.InterfaceGuid,
                AdapterDescription = current.InterfaceDescription,
                Properties = changes.Select(property => new GamingNetworkPropertySnapshot
                {
                    RegistryKeyword = property.RegistryKeyword,
                    OriginalValue = property.CurrentValue,
                }).ToList(),
            };
            journals.Add(journal);
            journalChanged = true;
        }
        else
        {
            foreach (var change in changes)
            {
                if (journal.Properties.All(property => property.RegistryKeyword != change.RegistryKeyword))
                {
                    journal.Properties.Add(new GamingNetworkPropertySnapshot
                    {
                        RegistryKeyword = change.RegistryKeyword,
                        OriginalValue = change.CurrentValue,
                    });
                    journalChanged = true;
                }
            }
        }

        if (journalChanged && !await TrySaveJournalAsync(beforeSave, results, "適用前の復元情報", ct))
        {
            return results;
        }

        foreach (var change in changes)
        {
            var write = await executor.RunAsync(
                WindowsGamingNetworkProfileCommandBuilder.FileName,
                WindowsGamingNetworkProfileCommandBuilder.BuildSetPropertyArguments(
                    current.AdapterName,
                    current.InterfaceGuid,
                    current.InterfaceDescription,
                    current.InterfaceType,
                    current.DriverProvider,
                    current.PnpDeviceId,
                    change.RegistryKeyword,
                    change.CurrentValue,
                    TargetValue),
                ct);
            results.Add(write);
            if (!write.Success)
            {
                results.Add(Failure(
                    "ゲーム向け NIC 設定: 適用中断",
                    "一部の変更に失敗しました。復元情報は保持しています。PC 再起動はまだ推奨しません。"));
                return results;
            }
        }

        var verified = await QueryAsync(current.AdapterName, "ゲーム向け NIC 設定: 適用後確認", results, ct);
        if (!HasSameIdentity(current, verified) ||
            current.Properties.Any(expected =>
                verified!.Properties.SingleOrDefault(property => property.RegistryKeyword == expected.RegistryKeyword)?.CurrentValue != TargetValue))
        {
            results.Add(Failure(
                "ゲーム向け NIC 設定: 適用後確認",
                "適用値 0 を読み戻し確認できませんでした。復元情報は保持しています。"));
            return results;
        }

        results.Add(Success(
            "ゲーム向け NIC 設定: 適用完了",
            BuildCompletionMessage("保存済みの復元情報と適用値 0 を読み戻し確認済みです。", current, missing)));
        return results;
    }

    public async Task<IReadOnlyList<CommandExecutionResult>> RestoreAsync(
        string adapterName,
        CancellationToken ct = default)
    {
        using var lease = await mutationGate.EnterAsync(ct);
        var results = new List<CommandExecutionResult>();
        var current = await QueryAsync(adapterName, "ゲーム向け NIC 設定: 復元前確認", results, ct);
        if (current is null)
        {
            return results;
        }

        if (!IsSupportedPhysicalAdapter(current))
        {
            results.Add(Failure(
                "ゲーム向け NIC 設定: 対象外",
                "対応する物理 Ethernet / Wi-Fi ではないため、変更していません。"));
            return results;
        }

        var journals = EnsureJournalList();
        if (ContainsNullJournalData(journals))
        {
            results.Add(Failure("ゲーム向け NIC 設定: 復元情報確認", "保存済みの復元情報に null 項目があります。変更していません。"));
            return results;
        }

        var matches = journals.Where(snapshot => snapshot.AdapterGuid == current.InterfaceGuid).ToArray();
        if (matches.Length == 0)
        {
            results.Add(Failure("ゲーム向け NIC 設定: 復元情報なし", "この NIC に対応する保存済みの復元情報はありません。変更していません。"));
            return results;
        }

        if (matches.Length > 1)
        {
            results.Add(Failure("ゲーム向け NIC 設定: 復元情報確認", "同じ NIC GUID の復元情報が重複しています。変更していません。"));
            return results;
        }

        var journal = matches[0];
        if (!TryValidateJournal(journal, current, out var journalError))
        {
            results.Add(Failure("ゲーム向け NIC 設定: 復元情報確認", journalError));
            return results;
        }

        var writes = new List<(GamingNetworkPropertySnapshot Snapshot, string CurrentValue)>();
        foreach (var snapshot in journal.Properties)
        {
            var property = current.Properties.SingleOrDefault(value => value.RegistryKeyword == snapshot.RegistryKeyword);
            if (property is null)
            {
                results.Add(Failure(
                    "ゲーム向け NIC 設定: 復元競合",
                    $"{snapshot.RegistryKeyword} が現在の NIC から取得できません。復元情報を保持し、変更していません。"));
                return results;
            }

            if (property.CurrentValue == snapshot.OriginalValue)
            {
                continue;
            }

            if (property.CurrentValue != TargetValue)
            {
                results.Add(Failure(
                    "ゲーム向け NIC 設定: 復元競合",
                    $"{snapshot.RegistryKeyword} は Shisui の適用値 0 と保存値 {snapshot.OriginalValue} のどちらでもありません。外部変更を上書きしません。"));
                return results;
            }

            writes.Add((snapshot, property.CurrentValue));
        }

        foreach (var (snapshot, currentValue) in writes)
        {
            var write = await executor.RunAsync(
                WindowsGamingNetworkProfileCommandBuilder.FileName,
                WindowsGamingNetworkProfileCommandBuilder.BuildSetPropertyArguments(
                    current.AdapterName,
                    current.InterfaceGuid,
                    current.InterfaceDescription,
                    current.InterfaceType,
                    current.DriverProvider,
                    current.PnpDeviceId,
                    snapshot.RegistryKeyword,
                    currentValue,
                    snapshot.OriginalValue),
                ct);
            results.Add(write);
            if (!write.Success)
            {
                results.Add(Failure(
                    "ゲーム向け NIC 設定: 復元中断",
                    "一部の復元に失敗しました。残りを推測して変更せず、復元情報を保持しています。"));
                return results;
            }
        }

        var verified = await QueryAsync(current.AdapterName, "ゲーム向け NIC 設定: 復元後確認", results, ct);
        if (!HasSameIdentity(current, verified) ||
            journal.Properties.Any(snapshot =>
                verified!.Properties.SingleOrDefault(property => property.RegistryKeyword == snapshot.RegistryKeyword)?.CurrentValue != snapshot.OriginalValue))
        {
            results.Add(Failure(
                "ゲーム向け NIC 設定: 復元後確認",
                "保存した元の値を読み戻し確認できませんでした。復元情報は保持しています。"));
            return results;
        }

        var beforeSave = CloneJournals(journals);
        journals.Remove(journal);
        if (!await TrySaveJournalAsync(beforeSave, results, "復元済み情報の削除", ct))
        {
            return results;
        }

        results.Add(Success(
            "ゲーム向け NIC 設定: 復元完了",
            BuildCompletionMessage("保存済みの元の設定値を読み戻し確認し、復元情報の削除も保存しました。", current, [])));
        return results;
    }

    private async Task<GamingNetworkAdapterState?> QueryAsync(
        string adapterName,
        string operation,
        List<CommandExecutionResult> results,
        CancellationToken ct)
    {
        string arguments;
        try
        {
            arguments = WindowsGamingNetworkProfileCommandBuilder.BuildQueryArguments(adapterName);
        }
        catch (ArgumentException ex)
        {
            results.Add(Failure(operation, ex.Message));
            return null;
        }

        var query = await executor.RunAsync(WindowsGamingNetworkProfileCommandBuilder.FileName, arguments, ct);
        results.Add(query);
        if (!query.Success)
        {
            results.Add(Failure(operation, "指定名に一致する NIC を一意に取得できず、状態の読み取りに失敗しました。"));
            return null;
        }

        if (!WindowsGamingNetworkProfileParser.TryParse(query.StandardOutput, out var state, out var error))
        {
            results.Add(Failure(operation, $"{error} 状態の読み取りに失敗しました。"));
            return null;
        }

        return state;
    }

    private List<GamingNetworkProfileSnapshot> EnsureJournalList()
    {
        settingsService.Current.GamingNetworkProfileSnapshots ??= [];
        return settingsService.Current.GamingNetworkProfileSnapshots;
    }

    private async Task<bool> TrySaveJournalAsync(
        List<GamingNetworkProfileSnapshot> rollbackValue,
        List<CommandExecutionResult> results,
        string action,
        CancellationToken ct)
    {
        try
        {
            await settingsService.SaveAsync(ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            settingsService.Current.GamingNetworkProfileSnapshots = rollbackValue;
            throw;
        }
        catch (Exception ex)
        {
            settingsService.Current.GamingNetworkProfileSnapshots = rollbackValue;
            results.Add(Failure(
                $"ゲーム向け NIC 設定: {action}失敗",
                $"設定ファイルへ保存できないため NIC はこれ以上変更していません: {ex.Message}"));
            return false;
        }
    }

    private static bool TryValidateJournal(
        GamingNetworkProfileSnapshot journal,
        GamingNetworkAdapterState current,
        out string error)
    {
        if (journal.AdapterGuid == Guid.Empty || journal.AdapterGuid != current.InterfaceGuid ||
            !string.Equals(journal.AdapterDescription, current.InterfaceDescription, StringComparison.Ordinal))
        {
            error = "保存済みの NIC GUID / 説明と現在の NIC が一致しません。変更していません。";
            return false;
        }

        if (journal.Properties is null || journal.Properties.Count == 0)
        {
            error = "保存済みの復元プロパティが空です。変更していません。";
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in journal.Properties)
        {
            if (property is null ||
                !WindowsGamingNetworkProfilePolicy.IsKeywordAllowed(
                    current.HardwareInterface,
                    current.InterfaceType,
                    current.DriverProvider,
                    current.PnpDeviceId,
                    property.RegistryKeyword) ||
                !seen.Add(property.RegistryKeyword) ||
                property.OriginalValue is not ("0" or "1"))
            {
                error = "保存済みの復元プロパティが対象外・重複・不正値を含みます。変更していません。";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool IsSupportedPhysicalAdapter(GamingNetworkAdapterState state) =>
        WindowsGamingNetworkProfilePolicy.IsSupportedPhysicalAdapter(state.HardwareInterface, state.InterfaceType);

    private static bool ContainsNullJournalData(IEnumerable<GamingNetworkProfileSnapshot> journals) =>
        journals.Any(snapshot => snapshot is null ||
            snapshot.Properties is null || snapshot.Properties.Any(property => property is null));

    private static bool HasSameIdentity(GamingNetworkAdapterState expected, GamingNetworkAdapterState? actual) =>
        actual is not null && IsSupportedPhysicalAdapter(actual) &&
        expected.InterfaceGuid == actual.InterfaceGuid &&
        expected.InterfaceType == actual.InterfaceType &&
        string.Equals(expected.InterfaceDescription, actual.InterfaceDescription, StringComparison.Ordinal) &&
        string.Equals(expected.DriverProvider, actual.DriverProvider, StringComparison.Ordinal) &&
        string.Equals(expected.PnpDeviceId, actual.PnpDeviceId, StringComparison.OrdinalIgnoreCase);

    private static string BuildCompletionMessage(
        string prefix,
        GamingNetworkAdapterState state,
        IReadOnlyList<string> missing)
    {
        var unsupported = missing.Count == 0
            ? string.Empty
            : $" ドライバーに存在しない {string.Join(" / ", missing)} は非対応としてスキップしました。";
        var powerTradeoff = state.InterfaceType == WindowsGamingNetworkProfilePolicy.WifiInterfaceType
            ? " Wi-Fi の省電力機能を無効化する設定では、消費電力の増加とバッテリー駆動時間の短縮があり得ます。"
            : string.Empty;
        return $"{prefix}{unsupported}{powerTradeoff} -NoRestart で保存したため、PC の再起動が必要です。実際のゲーム性能・遅延は未検証です。";
    }

    private static List<GamingNetworkProfileSnapshot> CloneJournals(IEnumerable<GamingNetworkProfileSnapshot> source) =>
        source.Select(snapshot => new GamingNetworkProfileSnapshot
        {
            AdapterGuid = snapshot.AdapterGuid,
            AdapterDescription = snapshot.AdapterDescription,
            Properties = snapshot.Properties?.Select(property => new GamingNetworkPropertySnapshot
            {
                RegistryKeyword = property.RegistryKeyword,
                OriginalValue = property.OriginalValue,
            }).ToList() ?? [],
        }).ToList();

    private static CommandExecutionResult Success(string command, string message) =>
        new(true, command, 0, message, string.Empty);

    private static CommandExecutionResult Failure(string command, string message) =>
        new(false, command, -1, string.Empty, message);
}
