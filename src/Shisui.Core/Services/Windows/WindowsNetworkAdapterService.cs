using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsNetworkAdapterService : INetworkAdapterService
{
    public Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken ct = default)
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();

        // フィルタ/バインディングの子インターフェイス判定に一覧全体の Description が要るため先に集める。
        var allDescriptions = interfaces.Select(ni => ni.Description).ToList();

        var adapters = interfaces
            .Where(ni => WindowsNetworkAdapterFilter.IsUserConfigurable(
                ni.Description, ni.NetworkInterfaceType, ni.OperationalStatus, allDescriptions))
            .Select(ToAdapterInfo)
            .OrderByDescending(a => a.IsUp)
            .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<IReadOnlyList<NetworkAdapterInfo>>(adapters);
    }

    private static NetworkAdapterInfo ToAdapterInfo(NetworkInterface ni)
    {
        List<string> ipv4Dns = [];
        List<string> ipv6Dns = [];

        try
        {
            var dnsAddresses = ni.GetIPProperties().DnsAddresses;
            ipv4Dns = dnsAddresses.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => a.ToString()).ToList();
            ipv6Dns = dnsAddresses.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                .Select(a => a.ToString()).ToList();
        }
        catch (NetworkInformationException)
        {
            // アダプタによっては IPProperties の取得に失敗することがある (仮想アダプタ等) → DNS 情報なしで続行
        }

        // netsh の name= が期待する接続名 (ncpa.cpl に表示される名前) は NetworkInterface.Name と一致する。
        return new NetworkAdapterInfo(
            Id: ni.Name,
            DisplayName: ni.Name,
            Description: ni.Description,
            IsUp: ni.OperationalStatus == OperationalStatus.Up,
            CurrentIPv4Dns: ipv4Dns,
            CurrentIPv6Dns: ipv6Dns);
    }
}
