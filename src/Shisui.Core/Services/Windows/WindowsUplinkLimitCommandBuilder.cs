using System.Globalization;
using System.Text;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// 上り速度制限 (ActiveStore の QoS ポリシー) の読み取り・書き込みコマンド生成と出力解析。純粋関数。
/// 出力は英語固定の KEY=VALUE。名前・NIC 名は UTF-8 の Base64 で出し、区切り文字や改行の混入を防ぐ。
/// スクリプトへ埋め込むのは固定名と数値だけ。
/// </summary>
public static class WindowsUplinkLimitCommandBuilder
{
    public const string FileName = "powershell";

    private const string Prelude =
        "$ErrorActionPreference='Stop';$ci=[Globalization.CultureInfo]::InvariantCulture;" +
        "function B($s){[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([string]$s))};";

    private const string PolicyReadback =
        "$c=0;foreach($p in @(Get-NetQosPolicy -PolicyStore ActiveStore)){if($p.Name -ieq $name){$c=$c+1;" +
        "'POLICY='+(B $p.Name)+','+([long]$p.ThrottleRate).ToString($ci)+','+[string]$p.Template}};'COUNT='+$c.ToString($ci)";

    // 既定経路 (IPv4、なければ IPv6) の NIC とゲートウェイ MAC。名前解決・外部通信はせず、近隣キャッシュを読むだけ。
    private const string LineLookup =
        "function L($prefix,$af){" +
        "$rs=@(Get-NetRoute -DestinationPrefix $prefix -AddressFamily $af -ErrorAction SilentlyContinue);" +
        "if($rs.Count -eq 0){return 'NONE'};" +
        "$min=[long]::MaxValue;$best=@();" +
        "foreach($r in $rs){$im=@(Get-NetIPInterface -InterfaceIndex $r.InterfaceIndex -AddressFamily $af -ErrorAction SilentlyContinue);" +
        "if($im.Count -ne 1){return 'LINE=UNKNOWN'};" +
        "$m=[long]$r.RouteMetric+[long]$im[0].InterfaceMetric;" +
        "if($m -lt $min){$min=$m;$best=@($r)}elseif($m -eq $min){$best+=$r}};" +
        "if($best.Count -ne 1){return 'LINE=UNKNOWN'};$r=$best[0];" +
        "if($r.NextHop -eq '0.0.0.0' -or $r.NextHop -eq '::'){return 'LINE=UNKNOWN'};" +
        "$n=@(Get-NetNeighbor -InterfaceIndex $r.InterfaceIndex -AddressFamily $af -IPAddress $r.NextHop -ErrorAction SilentlyContinue|" +
        "Where-Object{$_.State -in 'Reachable','Stale','Delay','Probe','Permanent'});" +
        "if($n.Count -ne 1){return 'LINE=UNKNOWN'};" +
        "$mac=([string]$n[0].LinkLayerAddress).ToUpperInvariant();" +
        "if($mac -notmatch '^([0-9A-F]{2}-){5}[0-9A-F]{2}$' -or $mac -eq '00-00-00-00-00-00' -or $mac -eq 'FF-FF-FF-FF-FF-FF'){return 'LINE=UNKNOWN'};" +
        "$a=@(Get-NetAdapter -InterfaceIndex $r.InterfaceIndex -ErrorAction SilentlyContinue);" +
        "if($a.Count -ne 1){return 'LINE=UNKNOWN'};" +
        "$g=[guid]$a[0].InterfaceGuid;if($g -eq [guid]::Empty){return 'LINE=UNKNOWN'};" +
        "$k='Physical';if($a[0].Virtual -or $a[0].HardwareInterface -ne $true){$k='Virtual'};" +
        "return 'LINE=OK,'+$g.ToString('B').ToUpperInvariant()+','+(B $a[0].Name)+','+$k+','+$mac};" +
        "$x=L '0.0.0.0/0' 'IPv4';if($x -eq 'NONE'){$x=L '::/0' 'IPv6'};if($x -eq 'NONE'){$x='LINE=UNKNOWN'};$x;";

    public static string BuildQueryArguments()
    {
        var script = Prelude + LineLookup +
            "foreach($p in @(Get-NetQosPolicy -PolicyStore ActiveStore)){" +
            "'QOSACTIVE='+(B $p.Name)+','+([long]$p.ThrottleRate).ToString($ci)+','+[string]$p.Template};" +
            "foreach($p in @(Get-NetQosPolicy)){'QOSDEFAULT='+(B $p.Name)};'DONE=1'";
        return WindowsPowerShellArgumentEncoder.Encode(script);
    }

    /// <summary>
    /// 固定名のポリシーを置換作成して読み戻す。ActiveStore または既定ストアに他のポリシーがある、
    /// または試験用と適用用が同時になる場合はスクリプト側でも拒否する。
    /// </summary>
    public static string BuildApplyArguments(string policyName, long bitsPerSecond)
    {
        var otherName = RequireKnownName(policyName).Equals(UplinkLimitPolicy.LimitPolicyName, StringComparison.OrdinalIgnoreCase)
            ? UplinkLimitPolicy.TrialPolicyName : UplinkLimitPolicy.LimitPolicyName;
        var canonical = Canonical(policyName);
        if (!UplinkLimitPolicy.IsValidRate(bitsPerSecond))
            throw new ArgumentOutOfRangeException(nameof(bitsPerSecond));
        var rate = bitsPerSecond.ToString(CultureInfo.InvariantCulture);
        var script = Prelude + $"$name='{canonical}';$other='{otherName}';$rate=[long]{rate};" +
            "foreach($p in @(Get-NetQosPolicy -PolicyStore ActiveStore)){" +
            "if($p.Name -ieq $other){throw 'Conflicting Shisui policy exists'};" +
            "if($p.Name -ine $name){throw 'Other QoS policy exists'}};" +
            "if(@(Get-NetQosPolicy).Count -ne 0){throw 'Other QoS policy exists in default store'};" +
            "foreach($p in @(Get-NetQosPolicy -PolicyStore ActiveStore)){if($p.Name -ieq $name){" +
            "Remove-NetQosPolicy -Name $p.Name -PolicyStore ActiveStore -Confirm:$false}};" +
            "New-NetQosPolicy -Name $name -Default -ThrottleRateActionBitsPerSecond $rate -PolicyStore ActiveStore -Confirm:$false|Out-Null;" +
            PolicyReadback;
        return WindowsPowerShellArgumentEncoder.Encode(script);
    }

    /// <summary>固定名のポリシーだけを削除して読み戻す (他のポリシーには触れない)。</summary>
    public static string BuildRemoveArguments(string policyName)
    {
        var canonical = Canonical(policyName);
        var script = Prelude + $"$name='{canonical}';" +
            "foreach($p in @(Get-NetQosPolicy -PolicyStore ActiveStore)){if($p.Name -ieq $name){" +
            "Remove-NetQosPolicy -Name $p.Name -PolicyStore ActiveStore -Confirm:$false}};" +
            PolicyReadback;
        return WindowsPowerShellArgumentEncoder.Encode(script);
    }

    /// <summary>
    /// <see cref="BuildQueryArguments"/> の出力を解釈する。LINE と DONE は 1 回ずつで DONE が最後。
    /// 欠落・重複・不正値・未知の行は Unreadable。
    /// </summary>
    public static UplinkLimitState ParseState(string output)
    {
        var lines = SplitLines(output);
        if (lines is null || lines.Count == 0 || lines[^1] != "DONE=1")
            return UplinkLimitState.Unreadable("QoS ポリシーの出力を解釈できませんでした。");

        UplinkLineIdentity? line = null;
        UplinkPolicyInfo? limit = null, trial = null;
        List<string> otherActive = [], otherDefault = [];
        HashSet<string> activeNames = new(StringComparer.OrdinalIgnoreCase), defaultNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines.Take(lines.Count - 1))
        {
            var separator = raw.IndexOf('=');
            if (separator <= 0) return UplinkLimitState.Unreadable("QoS ポリシーの出力を解釈できませんでした。");
            var key = raw[..separator];
            var value = raw[(separator + 1)..];
            switch (key)
            {
                case "LINE":
                    if (line is not null || !TryParseLine(value, out line))
                        return UplinkLimitState.Unreadable("回線識別の出力を解釈できませんでした。");
                    break;
                case "QOSACTIVE":
                    // 名前だけを先に取り出す。Shisui 以外のポリシーは名前の表示だけが目的なので、Template などは検証しない。
                    if (!TryDecode(value.Split(',')[0], out var activeName) || activeName.Length == 0 || !activeNames.Add(activeName))
                        return UplinkLimitState.Unreadable("QoS ポリシーの出力を解釈できませんでした。");
                    if (!UplinkLimitPolicy.IsKnownName(activeName))
                    {
                        otherActive.Add(activeName);
                        break;
                    }

                    // 自名のポリシーは厳格に解釈する。
                    if (!TryParsePolicy(value, out var policy))
                        return UplinkLimitState.Unreadable("QoS ポリシーの出力を解釈できませんでした。");
                    if (policy.Name.Equals(UplinkLimitPolicy.LimitPolicyName, StringComparison.OrdinalIgnoreCase)) limit = policy;
                    else trial = policy;
                    break;
                case "QOSDEFAULT":
                    // 既定ストアには何も書かないため、Shisui 名を含めすべて他のポリシー扱い。
                    if (!TryDecode(value, out var name) || !defaultNames.Add(name))
                        return UplinkLimitState.Unreadable("QoS ポリシーの出力を解釈できませんでした。");
                    otherDefault.Add(name);
                    break;
                default:
                    return UplinkLimitState.Unreadable("QoS ポリシーの出力を解釈できませんでした。");
            }
        }

        return line is null
            ? UplinkLimitState.Unreadable("回線識別の出力がありませんでした。")
            : new UplinkLimitState(UplinkStateReadStatus.Ok, line, limit, trial, otherActive, otherDefault);
    }

    /// <summary>
    /// 書き込み・削除後の読み戻し (POLICY 行 n 件と COUNT=n)。解釈できなければ null。
    /// </summary>
    public static IReadOnlyList<UplinkPolicyInfo>? ParsePolicyReadback(string output)
    {
        var lines = SplitLines(output);
        if (lines is null || lines.Count == 0 || !lines[^1].StartsWith("COUNT=", StringComparison.Ordinal))
            return null;
        var policies = new List<UplinkPolicyInfo>();
        foreach (var raw in lines.Take(lines.Count - 1))
        {
            if (!raw.StartsWith("POLICY=", StringComparison.Ordinal) || !TryParsePolicy(raw["POLICY=".Length..], out var policy))
                return null;
            policies.Add(policy);
        }

        var countText = lines[^1]["COUNT=".Length..];
        return int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            && count == policies.Count && countText == count.ToString(CultureInfo.InvariantCulture) ? policies : null;
    }

    private static string RequireKnownName(string name) =>
        UplinkLimitPolicy.IsKnownName(name) ? name : throw new ArgumentException("固定名以外のポリシーは操作できません。", nameof(name));

    private static string Canonical(string name) =>
        RequireKnownName(name).Equals(UplinkLimitPolicy.LimitPolicyName, StringComparison.OrdinalIgnoreCase)
            ? UplinkLimitPolicy.LimitPolicyName : UplinkLimitPolicy.TrialPolicyName;

    // 終端の空行だけを許し、途中の空行は不正とする。
    private static List<string>? SplitLines(string output)
    {
        var lines = output.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines.Any(line => line.Length == 0) ? null : lines;
    }

    private static bool TryParseLine(string value, out UplinkLineIdentity? line)
    {
        line = null;
        if (value == "UNKNOWN")
        {
            line = UplinkLineIdentity.Unknown;
            return true;
        }

        var parts = value.Split(',');
        if (parts.Length != 5 || parts[0] != "OK" || !Guid.TryParseExact(parts[1], "B", out var guid) || guid == Guid.Empty
            || !TryDecode(parts[2], out var name) || name.Length == 0 || !IsMac(parts[4]))
            return false;
        if (!Enum.TryParse(parts[3], false, out UplinkAdapterKind kind) || !Enum.IsDefined(kind))
            return false;
        line = new UplinkLineIdentity(UplinkLineStatus.Identified, name, guid.ToString("B").ToUpperInvariant(), kind, parts[4]);
        return true;
    }

    private static bool IsMac(string value) =>
        value.Length == 17 && value != "00-00-00-00-00-00" && value != "FF-FF-FF-FF-FF-FF"
        && value.Select((c, i) => i % 3 == 2 ? c == '-' : char.IsAsciiHexDigitUpper(c) || char.IsAsciiDigit(c)).All(ok => ok);

    private static bool TryParsePolicy(string value, out UplinkPolicyInfo policy)
    {
        policy = null!;
        var parts = value.Split(',');
        if (parts.Length != 3 || !TryDecode(parts[0], out var name) || name.Length == 0
            || !long.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var rate) || rate < -1
            || parts[1] != rate.ToString(CultureInfo.InvariantCulture))
            return false;
        if (parts[2] is not ("Default" or "None")) return false;
        policy = new UplinkPolicyInfo(name, rate, parts[2] == "Default");
        return true;
    }

    private static bool TryDecode(string base64, out string value)
    {
        value = string.Empty;
        try
        {
            value = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(base64));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
