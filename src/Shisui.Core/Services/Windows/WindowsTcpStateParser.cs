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
        var legacyProviderValues = new List<string>();
        var providers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var autoTuningLevel = string.Empty;
        var autoTuningLevelGroupPolicy = string.Empty;
        var autoTuningLevelEffective = string.Empty;
        var hasInvalidProviderData = false;
        var hasLegacyProviderData = false;
        var hasNamedProviderData = false;

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
                case "AUTOTUNE_POLICY": autoTuningLevelGroupPolicy = value; break;
                case "AUTOTUNE_SOURCE": autoTuningLevelEffective = value; break;
                case "CC":
                    if (value.Length == 0)
                    {
                        hasInvalidProviderData = true;
                        break;
                    }

                    var separator = value.IndexOf('|');
                    if (separator >= 0)
                    {
                        hasNamedProviderData = true;
                        if (separator == 0 ||
                            separator == value.Length - 1 ||
                            value.IndexOf('|', separator + 1) >= 0)
                        {
                            hasInvalidProviderData = true;
                            break;
                        }

                        var template = value[..separator].Trim();
                        var provider = value[(separator + 1)..].Trim();
                        if (template.Length == 0 || provider.Length == 0)
                        {
                            hasInvalidProviderData = true;
                            break;
                        }

                        if (!WindowsTcpCommandBuilder.SupplementalTemplates.Contains(
                                template, StringComparer.OrdinalIgnoreCase) ||
                            providers.ContainsKey(template))
                        {
                            hasInvalidProviderData = true;
                        }

                        // 状態判定が Unknown になる場合でも、復元に使える名前付き取得値は保持する。
                        providers[template] = provider;
                    }
                    else
                    {
                        hasLegacyProviderData = true;
                        legacyProviderValues.Add(value);
                    }

                    break;
            }
        }

        var bbr2 = ResolveBbr2(
            legacyProviderValues,
            providers,
            hasLegacyProviderData,
            hasNamedProviderData,
            hasInvalidProviderData);

        return new TcpSettingsSnapshot(
            bbr2,
            options,
            autoTuningLevel,
            providers,
            autoTuningLevelGroupPolicy,
            autoTuningLevelEffective);
    }

    private static Bbr2Status ResolveBbr2(
        IReadOnlyList<string> legacyProviders,
        IReadOnlyDictionary<string, string> namedProviders,
        bool hasLegacyProviderData,
        bool hasNamedProviderData,
        bool hasInvalidProviderData)
    {
        if (hasInvalidProviderData ||
            hasLegacyProviderData == hasNamedProviderData)
        {
            return Bbr2Status.Unknown;
        }

        IReadOnlyList<string> providers;
        if (hasNamedProviderData)
        {
            if (namedProviders.Count != WindowsTcpCommandBuilder.SupplementalTemplates.Count ||
                WindowsTcpCommandBuilder.SupplementalTemplates.Any(
                    template => !namedProviders.ContainsKey(template)))
            {
                return Bbr2Status.Unknown;
            }

            providers = WindowsTcpCommandBuilder.SupplementalTemplates
                .Select(template => namedProviders[template])
                .ToArray();
        }
        else
        {
            if (legacyProviders.Count != WindowsTcpCommandBuilder.SupplementalTemplates.Count)
            {
                return Bbr2Status.Unknown;
            }

            providers = legacyProviders;
        }

        var bbr2Count = providers.Count(p => p.Equals("BBR2", StringComparison.OrdinalIgnoreCase));

        if (bbr2Count == providers.Count)
        {
            return Bbr2Status.Enabled;
        }

        return bbr2Count == 0 ? Bbr2Status.Disabled : Bbr2Status.Partial;
    }
}
