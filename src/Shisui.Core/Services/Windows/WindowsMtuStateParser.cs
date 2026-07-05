namespace Shisui.Core.Services.Windows;

/// <summary>
/// <see cref="WindowsMtuStateCommandBuilder"/> が出力する MTU= 行をパースする純粋関数
/// (プロセス起動・OS 呼び出しを行わない、ユニットテスト対象)。
/// </summary>
public static class WindowsMtuStateParser
{
    public static int? Parse(string stdout)
    {
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

            if (key == "MTU" && int.TryParse(value, out var mtu))
            {
                return mtu;
            }
        }

        return null;
    }
}
