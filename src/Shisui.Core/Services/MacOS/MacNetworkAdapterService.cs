using System.Net;
using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.MacOS;

/// <summary>
/// macOS の「ネットワークサービス」(システム設定 > ネットワーク の各エントリ) を列挙する。
/// networksetup が扱う識別子は BSD デバイス名 (en0 等) ではなくこのサービス名である点に注意。
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacNetworkAdapterService(ICommandExecutor executor) : INetworkAdapterService
{
    public async Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken ct = default)
    {
        var listResult = await executor.RunAsync("networksetup", "-listallnetworkservices", ct);
        if (!listResult.Success)
        {
            return [];
        }

        // 先頭行は "An asterisk (*) denotes that a network service is disabled." という説明文なので除外し、
        // 無効化済み (先頭が *) のサービスも除外する。
        var serviceNames = listResult.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(1)
            .Where(line => !line.StartsWith('*'))
            .ToList();

        var adapters = new List<NetworkAdapterInfo>(serviceNames.Count);
        foreach (var name in serviceNames)
        {
            ct.ThrowIfCancellationRequested();
            var (ipv4, ipv6) = await GetCurrentDnsAsync(name, ct);
            adapters.Add(new NetworkAdapterInfo(name, name, null, IsUp: true, ipv4, ipv6));
        }

        return adapters;
    }

    /// <summary>
    /// networksetup が扱うサービス名からは MAC アドレス等が直接読めないため、
    /// -listnetworkserviceorder で裏の BSD デバイス名 (en0 等) に変換してから ifconfig に問い合わせる。
    /// </summary>
    public async Task<NetworkAdapterDetails?> GetAdapterDetailsAsync(string adapterId, CancellationToken ct = default)
    {
        var orderResult = await executor.RunAsync("networksetup", "-listnetworkserviceorder", ct);
        if (!orderResult.Success)
        {
            return null;
        }

        var serviceToDevice = MacNetworkServiceOrderParser.ParseServiceNameToDevice(orderResult.StandardOutput);
        if (!serviceToDevice.TryGetValue(adapterId, out var device))
        {
            return null;
        }

        var ifconfigResult = await executor.RunAsync("ifconfig", MacShellQuote.Quote(device), ct);
        return ifconfigResult.Success ? MacIfConfigParser.Parse(ifconfigResult.StandardOutput, adapterId) : null;
    }

    private async Task<(List<string> Ipv4, List<string> Ipv6)> GetCurrentDnsAsync(string serviceName, CancellationToken ct)
    {
        var result = await executor.RunAsync("networksetup", MacDnsCommandBuilder.BuildGetCurrent(serviceName), ct);
        if (!result.Success)
        {
            return ([], []);
        }

        // 未設定時は "There aren't any DNS Servers set on <service>." のような文が返るため、
        // IP アドレスとしてパースできる行だけを拾う。
        var ips = result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => IPAddress.TryParse(line, out _))
            .ToList();

        return (ips.Where(ip => !ip.Contains(':')).ToList(), ips.Where(ip => ip.Contains(':')).ToList());
    }
}
