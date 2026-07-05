using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// <see cref="WindowsDohStateCommandBuilder"/> が出力する KEY=VALUE 行をパースする純粋関数
/// (プロセス起動・OS 呼び出しを行わない、ユニットテスト対象)。
/// </summary>
public static class WindowsDohStateParser
{
    public static DohStatus Parse(string stdout, IReadOnlyList<string> expectedAddresses)
    {
        if (expectedAddresses.Count == 0)
        {
            return DohStatus.Unknown;
        }

        var autoUpgradeAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? currentServer = null;

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

            switch (key)
            {
                case "SERVER":
                    currentServer = value;
                    break;
                case "AUTOUPGRADE":
                    if (currentServer is not null && value.Equals("True", StringComparison.OrdinalIgnoreCase))
                    {
                        autoUpgradeAddresses.Add(currentServer);
                    }

                    break;
            }
        }

        var matchCount = expectedAddresses.Count(a => autoUpgradeAddresses.Contains(a));
        if (matchCount == expectedAddresses.Count)
        {
            return DohStatus.Enabled;
        }

        return matchCount == 0 ? DohStatus.Disabled : DohStatus.Partial;
    }
}
