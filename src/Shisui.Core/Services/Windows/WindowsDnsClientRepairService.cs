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
            && !result.RequiresOperationAbort && evidence.Length == 2
            && evidence.Count(x => x.StartsWith(marker + "|BASE|", StringComparison.Ordinal)) == 1
            && evidence.Count(x => x.StartsWith(marker + "|BOOT|", StringComparison.Ordinal)) == 1)
        {
            var fields = evidence.SingleOrDefault(x => x.StartsWith(marker + "|BASE|", StringComparison.Ordinal))?.Split('|') ?? [];
            if (fields.Length == 4 && fields[1] == "BASE"
                && long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
                && fields[3].Length == 64 && fields[3].All(Uri.IsHexDigit))
            {
                var bootLine = evidence.SingleOrDefault(x => x.StartsWith(marker + "|BOOT|", StringComparison.Ordinal));
                var boot = ParseBoot(bootLine, marker);
                if (boot is null && bootLine != marker + "|BOOT|UNKNOWN")
                    return new(Failed(result, "起動指紋の完了マーカーが不正です。"), null, null);
                return new(result with { StandardOutput = "DNS 検査開始前の System ログ境界を取得しました。"
                    + (boot is null ? " 起動指紋は取得不能です。再起動の確認ができるまで DNS 動作確認を保留します。" : " 起動イベント 12/27 の指紋を取得しました。") }, id, fields[3]) { BootFingerprint = boot };
            }
        }
        return new(Failed(result, "DNS 検査開始境界を確認できません。"), null, null);
    }

    public async Task<DnsClientBootObservation> CheckBootAsync(DnsClientBootFingerprint? previous, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (previous is not { IsValid: true })
            return new(CommandExecutionResult.Skipped("保存した起動指紋が不明なため、再起動を確認できません。"), DnsClientBootState.Unknown, null);
        var marker = "SHISUI_DNS_" + Guid.NewGuid().ToString("N");
        var result = await RunAsync(WindowsDnsClientRepairCommandBuilder.BuildBootCheck(previous, marker), ct);
        var lines = Lines(result.StandardOutput);
        var evidence = lines.Where(x => x.StartsWith(marker + "|", StringComparison.Ordinal)).ToArray();
        var current = evidence.Length == 1 ? ParseBoot(evidence[0], marker) : null;
        if (!result.Success || result.ExitCode != 0 || result.RequiresOperationAbort
            || !string.IsNullOrWhiteSpace(result.StandardError) || current is null)
            return new(Failed(result, "起動イベントの連続性または起動指紋を確認できません。"), DnsClientBootState.Unknown, null);
        var state = DnsClientBootFingerprint.Compare(previous, current, true);
        return new(result with { StandardOutput = state switch
        {
            DnsClientBootState.SameBoot => "同じ Windows 起動中です。DNS 動作確認は再起動後まで未確認です。",
            DnsClientBootState.ConfirmedNewBoot => "保存した起動境界以降の新しい Windows 起動を確認しました。",
            _ => "起動イベントの組が不整合です。DNS 動作確認を保留します。",
        }, Success = state != DnsClientBootState.Unknown }, state, current);
    }

    private static DnsClientBootFingerprint? ParseBoot(string? line, string marker)
    {
        var fields = line?.Split('|') ?? [];
        if (fields.Length != 6 || fields[0] != marker || fields[1] != "BOOT"
            || !long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var general)
            || !long.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out var boot)) return null;
        var fingerprint = new DnsClientBootFingerprint(general, fields[3], boot, fields[5]);
        return fingerprint.IsValid ? fingerprint : null;
    }

    public async Task<DnsClientRepairReport> RepairAndVerifyAsync(DnsServerSet servers,
        DnsClientRepairBaseline baseline, bool inspectDohEvents,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!baseline.Result.Success || baseline.Result.RequiresOperationAbort || baseline.RecordId is not > 0
            || baseline.XmlSha256 is not { Length: 64 } || !baseline.XmlSha256.All(Uri.IsHexDigit))
            return new(Failed(baseline.Result, "開始境界が不明なため DNS クライアントを検証できません。"), DnsClientVerificationStatus.Unknown);
        var marker = "SHISUI_DNS_" + Guid.NewGuid().ToString("N");
        var result = await RunAsync(WindowsDnsClientRepairCommandBuilder.BuildRepair(servers,
            baseline.RecordId.Value, baseline.XmlSha256, inspectDohEvents, marker), ct);
        var lines = Lines(result.StandardOutput);
        var evidence = lines.Where(x => x.StartsWith(marker + "|", StringComparison.Ordinal)).ToArray();
        var output = string.Join(Environment.NewLine, lines.Where(x => !x.StartsWith(marker + "|", StringComparison.Ordinal)));
        var status = evidence.Length == 1 ? evidence[0] : string.Empty;
        if (!result.RequiresOperationAbort && status == marker + "|RESULT|OK" && result.Success && result.ExitCode == 0
            && string.IsNullOrWhiteSpace(result.StandardError))
            return new(result with { StandardOutput = output }, DnsClientVerificationStatus.Verified);
        return new(Failed(result with { StandardOutput = output },
            status == marker + "|RESULT|FAIL" || status == marker + "|RESULT|UNKNOWN" ? "DNS クライアントの検証が完了していません。" : "DNS 検査結果の完了マーカーを確認できません。"),
            status == marker + "|RESULT|FAIL" && !result.RequiresOperationAbort ? DnsClientVerificationStatus.Failed : DnsClientVerificationStatus.Unknown);
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
