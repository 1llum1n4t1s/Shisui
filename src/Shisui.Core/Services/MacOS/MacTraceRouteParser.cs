using System.Text.RegularExpressions;
using Shisui.Core.Models;

namespace Shisui.Core.Services.MacOS;

/// <summary>
/// BSD traceroute (-n -q 1) の標準出力をパースする純粋関数 (プロセス起動・OS 呼び出しを行わない、
/// ユニットテスト対象)。ホップ行の書式 (" &lt;hop&gt;  &lt;ip&gt;  &lt;rtt&gt; ms" または
/// " &lt;hop&gt;  *" ) は固定の英語/数値書式でロケールに依存しない。応答が無いホップは
/// Address/RoundtripMs が null になる (通常のトレースルートでも起きる挙動)。
/// </summary>
public static class MacTraceRouteParser
{
    private static readonly Regex HopPattern = new(
        @"^\s*(\d+)\s+(?:(\S+)\s+([\d.]+)\s*ms|\*)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static IReadOnlyList<TraceRouteHop> Parse(string stdout)
    {
        var hops = new List<TraceRouteHop>();

        foreach (Match match in HopPattern.Matches(stdout))
        {
            var hopNumber = int.Parse(match.Groups[1].Value);
            var hasAddress = match.Groups[2].Success;
            var address = hasAddress ? match.Groups[2].Value : null;
            double? rtt = hasAddress && double.TryParse(match.Groups[3].Value, out var value) ? value : null;

            hops.Add(new TraceRouteHop(hopNumber, address, rtt));
        }

        return hops;
    }
}
