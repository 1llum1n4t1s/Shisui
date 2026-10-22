using System.Globalization;
using System.Text.RegularExpressions;
using Shisui.Core.Models;

namespace Shisui.Core.Services.MacOS;

/// <summary>
/// BSD ping の標準出力をパースする純粋関数 (プロセス起動・OS 呼び出しを行わない、ユニットテスト対象)。
/// BSD ping の "X packets transmitted, Y packets received" / "round-trip min/avg/max/stddev = ..."
/// は固定の英語書式でロケールに依存しない (macOS のシステム言語設定に関わらず変化しない)。
/// </summary>
public static partial class MacPingResultParser
{
    [GeneratedRegex(@"(\d+) packets transmitted,\s*(\d+) packets received")]
    private static partial Regex TransmittedPattern();

    [GeneratedRegex(@"round-trip min/avg/max/stddev = [\d.]+/([\d.]+)/[\d.]+/[\d.]+ ms")]
    private static partial Regex RoundTripPattern();

    public static PingResult Parse(string stdout, string host, int sent)
    {
        var transmittedMatch = TransmittedPattern().Match(stdout);
        var received = transmittedMatch.Success ? int.Parse(transmittedMatch.Groups[2].Value) : 0;

        var roundTripMatch = RoundTripPattern().Match(stdout);
        double? average = roundTripMatch.Success && double.TryParse(
            roundTripMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var avg)
            ? avg
            : null;

        return new PingResult(received > 0, host, sent, received, average, stdout);
    }
}
