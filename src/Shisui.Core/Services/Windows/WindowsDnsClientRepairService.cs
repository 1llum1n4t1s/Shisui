using System.Globalization;
using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsDnsClientRepairService(ICommandExecutor executor) : IDnsClientRepairService
{
    public async Task<DnsClientRepairBaseline> CaptureBaselineAsync(CancellationToken ct = default)
    {
        var marker = "SHISUI_DNS_" + Guid.NewGuid().ToString("N");
        var result = await RunAsync(WindowsDnsClientRepairCommandBuilder.BuildCapture(marker), ct);
        var lines = Lines(result.StandardOutput);
        var evidence = lines.Where(x => x.StartsWith(marker + "|", StringComparison.Ordinal)).ToArray();
        if (result.Success && result.ExitCode == 0 && string.IsNullOrWhiteSpace(result.StandardError)
            && !result.RequiresOperationAbort && evidence.Length == 1)
        {
            var fields = evidence[0].Split('|');
            if (fields.Length == 4 && fields[1] == "BASE"
                && long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
                && fields[3].Length == 64 && fields[3].All(Uri.IsHexDigit))
                return new(result with { StandardOutput = "DNS 検査開始前の System ログ境界を取得しました。" }, id, fields[3]);
        }
        return new(Failed(result, "DNS 検査開始境界を確認できません。"), null, null);
    }

    public async Task<DnsClientRepairReport> RepairAndVerifyAsync(DnsServerSet servers,
        DnsClientRepairBaseline baseline, bool inspectDohEvents, bool deferFailedResolution,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!baseline.Result.Success || baseline.Result.RequiresOperationAbort || baseline.RecordId is not > 0
            || baseline.XmlSha256 is not { Length: 64 } || !baseline.XmlSha256.All(Uri.IsHexDigit))
            return new(Failed(baseline.Result, "開始境界が不明なため DNS クライアントを検証できません。"), false);
        var marker = "SHISUI_DNS_" + Guid.NewGuid().ToString("N");
        var result = await RunAsync(WindowsDnsClientRepairCommandBuilder.BuildRepair(servers,
            baseline.RecordId.Value, baseline.XmlSha256, inspectDohEvents, deferFailedResolution, marker), ct);
        var lines = Lines(result.StandardOutput);
        var evidence = lines.Where(x => x.StartsWith(marker + "|", StringComparison.Ordinal)).ToArray();
        var output = string.Join(Environment.NewLine, lines.Where(x => !x.StartsWith(marker + "|", StringComparison.Ordinal)));
        var status = evidence.Length == 1 ? evidence[0] : string.Empty;
        if (!result.RequiresOperationAbort && status == marker + "|RESULT|OK" && result.Success && result.ExitCode == 0
            && string.IsNullOrWhiteSpace(result.StandardError))
            return new(result with { StandardOutput = output }, false);
        var deferred = !result.RequiresOperationAbort && !result.Success && result.ExitCode == 1
            && string.IsNullOrWhiteSpace(result.StandardError) && deferFailedResolution && status == marker + "|RESULT|DEFER";
        return new(Failed(result with { StandardOutput = output },
            status == marker + "|RESULT|FAIL" || deferred ? "DNS クライアントの検証が完了していません。" : "DNS 検査結果の完了マーカーを確認できません。"), deferred);
    }

    private async Task<CommandExecutionResult> RunAsync(string arguments, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try { return await executor.RunAsync("powershell", arguments, timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // ICommandExecutor 自体は終了確認を保証しないため、タイムアウト後の変更処理は止める。
            return CommandExecutionResult.Skipped("DNS クライアントの検査が 45 秒でタイムアウトしました。")
                with { RequiresOperationAbort = true };
        }
    }

    private static string[] Lines(string output) => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    private static CommandExecutionResult Failed(CommandExecutionResult result, string message) => result with
    {
        Success = false,
        StandardError = string.IsNullOrEmpty(result.StandardError) ? message : result.StandardError + Environment.NewLine + message,
    };
}
