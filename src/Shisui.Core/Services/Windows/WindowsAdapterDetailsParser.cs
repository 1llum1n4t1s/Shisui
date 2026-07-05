using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// <see cref="WindowsAdapterDetailsCommandBuilder"/> が出力する KEY=VALUE 行をパースする純粋関数
/// (プロセス起動・OS 呼び出しを行わない、ユニットテスト対象)。
/// </summary>
public static class WindowsAdapterDetailsParser
{
    public static NetworkAdapterDetails? Parse(string stdout, string adapterId)
    {
        string? mac = null;
        string? linkSpeed = null;
        string? mediaType = null;
        string? status = null;
        var found = false;

        foreach (var rawLine in stdout.Split('\n'))
        {
            var line = rawLine.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            found = true;
            var key = line[..eq].Trim().ToUpperInvariant();
            var value = line[(eq + 1)..].Trim();

            switch (key)
            {
                case "MAC": mac = value.Length > 0 ? value : null; break;
                case "LINKSPEED": linkSpeed = value.Length > 0 ? value : null; break;
                case "MEDIATYPE": mediaType = value.Length > 0 ? value : null; break;
                case "STATUS": status = value; break;
            }
        }

        if (!found)
        {
            return null;
        }

        return new NetworkAdapterDetails(adapterId, mac, linkSpeed, mediaType, string.Equals(status, "Up", StringComparison.OrdinalIgnoreCase));
    }
}
