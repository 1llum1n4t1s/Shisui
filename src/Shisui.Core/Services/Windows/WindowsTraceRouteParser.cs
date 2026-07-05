namespace Shisui.Core.Services.Windows;

/// <summary>
/// <see cref="WindowsTraceRouteCommandBuilder"/> が出力する HOP= 行をパースする純粋関数
/// (プロセス起動・OS 呼び出しを行わない、ユニットテスト対象)。ホップ番号順の IP アドレス一覧のみを返す
/// (往復時間は含まない。Service 層が別途 ping して埋める)。
/// </summary>
public static class WindowsTraceRouteParser
{
    public static IReadOnlyList<string> ParseHopAddresses(string stdout)
    {
        var addresses = new List<string>();

        foreach (var rawLine in stdout.Split('\n'))
        {
            var line = rawLine.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim().ToUpperInvariant();
            var value = line[(eq + 1)..].Trim();

            if (key == "HOP" && value.Length > 0)
            {
                addresses.Add(value);
            }
        }

        return addresses;
    }
}
