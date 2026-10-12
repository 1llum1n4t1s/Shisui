using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>解放の成否にかかわらず更新を試み、選択 NIC の DHCP 回復を読み戻す。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDhcpLeaseService(ICommandExecutor executor) : IDhcpLeaseService
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RenewTimeout = TimeSpan.FromSeconds(60);

    public async Task<IReadOnlyList<CommandExecutionResult>> ReacquireAsync(string adapterId, CancellationToken ct = default)
    {
        var results = new List<CommandExecutionResult>();
        string queryArguments;
        try
        {
            queryArguments = WindowsDhcpLeaseCommandBuilder.BuildQueryArguments(adapterId);
        }
        catch (ArgumentException ex)
        {
            results.Add(Failure("DHCP 再取得: 接続名確認", ex.Message));
            return results;
        }

        var query = await ExecuteAsync(queryArguments, "DHCP 再取得: 事前確認", ReadTimeout, ct);
        results.Add(query);
        if (!query.Success) return results;
        var before = WindowsDhcpLeaseStateParser.Parse(query.StandardOutput);
        if (before is null || !before.Connected)
        {
            results.Add(Failure("DHCP 再取得: 事前確認", "選択 NIC の状態を確定できないか、接続されていません。解放していません。"));
            return results;
        }

        foreach (var ipv6 in new[] { false, true })
        {
            var family = ipv6 ? "IPv6" : "IPv4";
            var dhcp = ipv6 ? before.Dhcp6 : before.Dhcp4;
            if (dhcp == "Unknown")
            {
                results.Add(Failure($"DHCP 再取得: {family} 確認不能", "DHCP の利用状態を確認できないため、解放・更新していません。"));
                continue;
            }

            if (dhcp != "Enabled" || (ipv6 && !before.HasLease6))
            {
                var reason = dhcp == "Disabled" ? "固定 IP / DHCP 無効" :
                    "DHCPv6 の有効な取得済みリースなし (SLAAC のみを含む)";
                results.Add(new(true, $"DHCP 再取得: {family} 対象外", 0, $"{reason}のため解放・更新していません。", string.Empty));
                continue;
            }

            if (ct.IsCancellationRequested)
            {
                results.Add(Failure($"DHCP 再取得: {family} 中止", "キャンセルされたため、この IP ファミリーは解放していません。"));
                return results;
            }

            // release に渡したトークンは回復で再利用しない。プロセスの例外・中断後も更新を必ず一度試す。
            var releaseArguments = WindowsDhcpLeaseCommandBuilder.BuildReleaseArguments(adapterId, before.InterfaceGuid, before.InterfaceIndex, ipv6);
            var renewArguments = WindowsDhcpLeaseCommandBuilder.BuildRenewArguments(adapterId, before.InterfaceGuid, before.InterfaceIndex, ipv6);
            try
            {
                results.Add(await ExecuteAsync(releaseArguments, $"DHCP 再取得: {family} 解放", ReleaseTimeout, ct));
            }
            finally
            {
                results.Add(await ExecuteAsync(renewArguments, $"DHCP 再取得: {family} 更新・回復", RenewTimeout, CancellationToken.None));
            }

            // ユーザーが中止していても、更新後の回復確認は独立した期限で行う。
            var after = await ReadBackAsync(adapterId, before, ipv6, results);
            if (after is null || !IsRecovered(before, after, ipv6))
            {
                results.Add(Failure($"DHCP 再取得: {family} 回復確認", "同じ NIC の DHCP 由来の有効な IP と、既存のデフォルトゲートウェイの回復を確認できません。接続状態を確認してください。"));
            }
            else
            {
                results.Add(new(true, $"DHCP 再取得: {family} 回復確認", 0,
                    "同じ NIC の DHCP 由来の有効な IP と、既存のデフォルトゲートウェイの回復を確認しました。IP の値は変更されない場合もあります。", string.Empty));
            }

            if (ct.IsCancellationRequested)
            {
                results.Add(Failure("DHCP 再取得: 中止", "キャンセルされました。解放を試みた IP ファミリーの更新・回復確認を実行してから中止しました。"));
                return results;
            }
        }

        return results;
    }

    private async Task<DhcpLeaseState?> ReadBackAsync(string adapterId, DhcpLeaseState before, bool ipv6, List<CommandExecutionResult> results)
    {
        var arguments = WindowsDhcpLeaseCommandBuilder.BuildQueryArguments(adapterId, before.InterfaceGuid, before.InterfaceIndex);
        // IPv6 の DAD (Tentative → Preferred) を待つ。全体を 15 秒に制限し、最後の観測も必ずログへ残す。
        using var deadline = new CancellationTokenSource(ReadTimeout);
        DhcpLeaseState? state = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var result = await ExecuteAsync(arguments, "DHCP 再取得: 更新後読取", ReadTimeout, deadline.Token);
            results.Add(result);
            if (!result.Success)
            {
                if (deadline.IsCancellationRequested)
                {
                    results.Add(Failure("DHCP 再取得: 更新後読取", "更新後の回復確認がタイムアウトしました。"));
                }
                return null;
            }
            state = WindowsDhcpLeaseStateParser.Parse(result.StandardOutput);
            if (state is null || !state.Connected || IsRecovered(before, state, ipv6)) return state;
            if (attempt < 4)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), deadline.Token);
                }
                catch (OperationCanceledException)
                {
                    results.Add(Failure("DHCP 再取得: 更新後読取", "更新後の回復待機がタイムアウトしました。"));
                    return null;
                }
            }
        }

        return state;
    }

    private static bool IsRecovered(DhcpLeaseState before, DhcpLeaseState after, bool ipv6) =>
        after.Connected && after.InterfaceGuid == before.InterfaceGuid && after.InterfaceIndex == before.InterfaceIndex &&
        (ipv6 ? after.Dhcp6 == "Enabled" && after.HasLease6 && (!before.HasGateway6 || after.HasGateway6) :
            after.Dhcp4 == "Enabled" && after.HasLease4 && (!before.HasGateway4 || after.HasGateway4));

    private async Task<CommandExecutionResult> ExecuteAsync(string arguments, string label, TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        try
        {
            var result = await executor.RunAsync("powershell", arguments, deadline.Token);
            if (!deadline.IsCancellationRequested) return result;
            return result with
            {
                Success = false,
                StandardError = result.StandardError + Environment.NewLine + (ct.IsCancellationRequested ? "処理がキャンセルされました。" : $"{label} がタイムアウトしました。"),
            };
        }
        catch (OperationCanceledException)
        {
            return Failure(label, ct.IsCancellationRequested ? "処理がキャンセルされました。" : $"{timeout.TotalSeconds:0} 秒でタイムアウトしました。");
        }
        catch (Exception ex)
        {
            return Failure(label, $"コマンドの実行に失敗しました: {ex.Message}");
        }
    }

    private static CommandExecutionResult Failure(string label, string error) => new(false, label, -1, string.Empty, error);
}
