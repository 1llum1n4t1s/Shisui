using System.Text.RegularExpressions;
using Shisui.Core.Models;

namespace Shisui.Core.Services.MacOS;

/// <summary>
/// BSD ping の標準出力をパースする純粋関数 (プロセス起動・OS 呼び出しを行わない、ユニットテスト対象)。
/// BSD ping の "X packets transmitted, Y packets received" / "round-trip min/avg/max/stddev = ..."
/// は固定の英語書式でロケールに依存しない (macOS のシステム言語設定に関わらず変化しない)。
/// </summary>
public static class MacPingResultParser
{
    private static readonly Regex TransmittedPattern = new(
        @"(\d+) packets transmitted,\s*(\d+) packets received", RegexOptions.Compiled);

    private static readonly Regex RoundTripPattern = new(
        @"round-trip min/avg/max/stddev = [\d.]+/([\d.]+)/[\d.]+/[\d.]+ ms", RegexOptions.Compiled);

    public static PingResult Parse(string stdout, string host, int sent)
    {
        var transmittedMatch = TransmittedPattern.Match(stdout);
        var received = transmittedMatch.Success ? int.Parse(transmittedMatch.Groups[2].Value) : 0;

        var roundTripMatch = RoundTripPattern.Match(stdout);
        double? average = roundTripMatch.Success && double.TryParse(roundTripMatch.Groups[1].Value, out var avg)
            ? avg
            : null;

        return new PingResult(received > 0, host, sent, received, average, stdout);
    }
}
