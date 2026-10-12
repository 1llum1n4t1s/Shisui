using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Shisui.Core.Services.Windows;

internal sealed record DhcpLeaseState(Guid InterfaceGuid, int InterfaceIndex, bool Connected,
    string Dhcp4, string Dhcp6, bool HasLease4, bool HasLease6, bool HasGateway4, bool HasGateway6);

/// <summary>言語に依存しないプロパティの完全な読取結果だけを受け入れる。</summary>
internal static class WindowsDhcpLeaseStateParser
{
    internal static DhcpLeaseState? Parse(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var addresses4 = new List<string>();
        var addresses6 = new List<string>();
        var gateways4 = new List<string>();
        var gateways6 = new List<string>();
        foreach (var raw in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            var separator = line.IndexOf('=');
            if (separator <= 0) return null;
            var key = line[..separator];
            var value = line[(separator + 1)..];
            switch (key)
            {
                case "IP4": addresses4.Add(value); break;
                case "IP6": addresses6.Add(value); break;
                case "GATEWAY4": gateways4.Add(value); break;
                case "GATEWAY6": gateways6.Add(value); break;
                default: if (!values.TryAdd(key, value)) return null; break;
            }
        }

        if (values.Count != 10 || !values.TryGetValue("COMPLETE", out var complete) || complete != "1" ||
            !values.TryGetValue("GUID", out var guidText) || !Guid.TryParse(guidText, out var guid) || guid == Guid.Empty ||
            !values.TryGetValue("INDEX", out var indexText) || !int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index <= 0 ||
            !values.TryGetValue("STATUS", out var status) || string.IsNullOrEmpty(status) ||
            !TryDhcp(values, "DHCP4", out var dhcp4) || !TryDhcp(values, "DHCP6", out var dhcp6) ||
            !HasExpectedCount(values, "IPCOUNT4", addresses4.Count) || !HasExpectedCount(values, "IPCOUNT6", addresses6.Count) ||
            !HasExpectedCount(values, "GATEWAYCOUNT4", gateways4.Count) || !HasExpectedCount(values, "GATEWAYCOUNT6", gateways6.Count) ||
            !TryAddresses(addresses4, AddressFamily.InterNetwork, out var lease4) ||
            !TryAddresses(addresses6, AddressFamily.InterNetworkV6, out var lease6) ||
            !TryGateways(gateways4, AddressFamily.InterNetwork) || !TryGateways(gateways6, AddressFamily.InterNetworkV6))
        {
            return null;
        }

        return new(guid, index, status == "Up", dhcp4, dhcp6, lease4, lease6, gateways4.Count > 0, gateways6.Count > 0);
    }

    private static bool TryDhcp(Dictionary<string, string> values, string key, out string value)
    {
        value = values.GetValueOrDefault(key, string.Empty);
        return value is "Enabled" or "Disabled" or "Unknown";
    }

    private static bool HasExpectedCount(Dictionary<string, string> values, string key, int actual) =>
        values.TryGetValue(key, out var text) && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count == actual;

    private static bool TryAddresses(List<string> addresses, AddressFamily family, out bool hasLease)
    {
        hasLease = false;
        foreach (var entry in addresses)
        {
            var parts = entry.Split('|');
            if (parts.Length != 3 || !IPAddress.TryParse(parts[0], out var address) || address.AddressFamily != family ||
                string.IsNullOrEmpty(parts[1]) || string.IsNullOrEmpty(parts[2])) return false;
            if (parts[1] == "Dhcp" && parts[2] == "Preferred" && IsUsableAddress(address)) hasLease = true;
        }

        return true;
    }

    private static bool TryGateways(List<string> gateways, AddressFamily family) =>
        gateways.All(text => IPAddress.TryParse(text, out var address) && address.AddressFamily == family &&
            !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any) && !IPAddress.IsLoopback(address));

    private static bool IsUsableAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            address.IsIPv6LinkLocal || address.IsIPv6Multicast) return false;
        if (address.AddressFamily != AddressFamily.InterNetwork) return true;
        var bytes = address.GetAddressBytes();
        return bytes[0] != 0 && bytes[0] < 224 && !(bytes[0] == 169 && bytes[1] == 254);
    }
}
