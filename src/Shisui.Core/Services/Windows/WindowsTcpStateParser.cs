using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// <see cref="WindowsTcpStateCommandBuilder"/> が出力する KEY=VALUE 行をパースする純粋関数
/// (プロセス起動・OS 呼び出しを行わない、ユニットテスト対象)。
/// </summary>
public static class WindowsTcpStateParser
{
    public static TcpSettingsSnapshot Parse(string stdout)
    {
        var options = new Dictionary<TcpGlobalOption, string>();
        var providerValues = new List<string>();
        var providers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var autoTuningLevel = string.Empty;

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
                case "RSS": options[TcpGlobalOption.Rss] = value; break;
                case "RSC": options[TcpGlobalOption.Rsc] = value; break;
                case "ECN": options[TcpGlobalOption.EcnCapability] = value; break;
                case "TIMESTAMPS": options[TcpGlobalOption.Timestamps] = value; break;
                case "FASTOPEN": options[TcpGlobalOption.FastOpen] = value; break;
                case "AUTOTUNE": autoTuningLevel = value; break;
                case "CC":
                    if (value.Length > 0)
                    {
                        var separator = value.IndexOf('|');
                        if (separator > 0 && separator < value.Length - 1)
                        {
                            var template = value[..separator].Trim();
                            var provider = value[(separator + 1)..].Trim();
                            if (template.Length > 0 && provider.Length > 0)
                            {
                                providers[template] = provider;
                                providerValues.Add(provider);
                            }
                        }
                        else
                        {
                            // 旧形式のテスト採取値もBBR2状態判定には引き続き利用する。
                            providerValues.Add(value);
                        }
                    }

                    break;
            }
        }

        return new TcpSettingsSnapshot(ResolveBbr2(providerValues), options, autoTuningLevel, providers);
    }

    private static Bbr2Status ResolveBbr2(IReadOnlyList<string> providers)
    {
        if (providers.Count == 0)
        {
            return Bbr2Status.Unknown;
        }

        var bbr2Count = providers.Count(p => p.Equals("BBR2", StringComparison.OrdinalIgnoreCase));

        if (bbr2Count == providers.Count)
        {
            return Bbr2Status.Enabled;
        }

        return bbr2Count == 0 ? Bbr2Status.Disabled : Bbr2Status.Partial;
    }
}
