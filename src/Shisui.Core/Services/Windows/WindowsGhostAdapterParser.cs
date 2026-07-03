using System.Xml;
using System.Xml.Linq;
using Shisui.Core.Models;

namespace Shisui.Core.Services.Windows;

/// <summary>
/// `pnputil /enum-devices ... /format xml` の出力をパースする純粋関数 (プロセス起動は行わない、ユニットテスト対象)。
/// </summary>
public static class WindowsGhostAdapterParser
{
    public static IReadOnlyList<GhostAdapterInfo> Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return [];
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (XmlException)
        {
            return [];
        }

        return doc.Root?.Elements("Device").Select(ToGhostAdapterInfo).ToList() ?? [];
    }

    private static GhostAdapterInfo ToGhostAdapterInfo(XElement device)
    {
        var instanceId = (string?)device.Attribute("InstanceId") ?? string.Empty;
        var description = (string?)device.Element("DeviceDescription") ?? string.Empty;
        var manufacturer = (string?)device.Element("ManufacturerName") ?? string.Empty;
        var driverName = (string?)device.Element("DriverName") ?? string.Empty;

        return new GhostAdapterInfo(
            instanceId,
            description,
            manufacturer,
            driverName,
            IsLikelyMicrosoftVirtualDevice: manufacturer.Contains("Microsoft", StringComparison.OrdinalIgnoreCase));
    }
}
