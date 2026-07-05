using System.Text.RegularExpressions;
using Shisui.Core.Models;

namespace Shisui.Core.Services.MacOS;

/// <summary>
/// <c>ifconfig &lt;device&gt;</c> の出力をパースする純粋関数 (プロセス起動・OS 呼び出しを行わない、
/// ユニットテスト対象)。ifconfig の出力は BSD 標準の固定英語書式でロケールに依存しない。
/// </summary>
public static class MacIfConfigParser
{
    private static readonly Regex EtherPattern = new(@"ether\s+([0-9a-fA-F:]{17})", RegexOptions.Compiled);
    private static readonly Regex StatusPattern = new(@"status:\s*(\S+)", RegexOptions.Compiled);
    private static readonly Regex MediaPattern = new(@"media:\s*(.+)", RegexOptions.Compiled);

    public static NetworkAdapterDetails? Parse(string stdout, string adapterId)
    {
        if (stdout.Length == 0)
        {
            return null;
        }

        var mac = Match(EtherPattern, stdout);
        var status = Match(StatusPattern, stdout);
        var media = Match(MediaPattern, stdout)?.Trim();

        return new NetworkAdapterDetails(
            adapterId,
            mac,
            media,
            MediaType: null,
            IsUp: string.Equals(status, "active", StringComparison.OrdinalIgnoreCase));
    }

    private static string? Match(Regex pattern, string input)
    {
        var match = pattern.Match(input);
        return match.Success ? match.Groups[1].Value : null;
    }
}
