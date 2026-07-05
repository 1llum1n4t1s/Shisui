using System.Text.RegularExpressions;

namespace Shisui.Core.Services.MacOS;

/// <summary>
/// <c>networksetup -listnetworkserviceorder</c> の出力をパースする純粋関数 (プロセス起動・OS 呼び出しを
/// 行わない、ユニットテスト対象)。ネットワークサービス名 (アプリが Id として使う値) と、その裏にある
/// BSD デバイス名 (en0 等、ifconfig で問い合わせる際に必要) の対応表を作る。出力は各サービスにつき
/// "(N) &lt;サービス名&gt;" の次の行に "(Hardware Port: ..., Device: &lt;device&gt;)" が続く形式で、
/// 無効化済みサービスは "*" が前置される (この記号だけ英語非依存なので安全に無視できる)。
/// </summary>
public static class MacNetworkServiceOrderParser
{
    private static readonly Regex ServiceLinePattern = new(@"^\(\d+\)\s*\*?(.+)$", RegexOptions.Compiled);
    private static readonly Regex DeviceLinePattern = new(@"Device:\s*([\w.]+)\)", RegexOptions.Compiled);

    public static IReadOnlyDictionary<string, string> ParseServiceNameToDevice(string stdout)
    {
        var map = new Dictionary<string, string>();
        var lines = stdout.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var serviceMatch = ServiceLinePattern.Match(lines[i].Trim());
            if (!serviceMatch.Success || i + 1 >= lines.Length)
            {
                continue;
            }

            var deviceMatch = DeviceLinePattern.Match(lines[i + 1].Trim());
            if (deviceMatch.Success)
            {
                map[serviceMatch.Groups[1].Value.Trim()] = deviceMatch.Groups[1].Value;
            }
        }

        return map;
    }
}
