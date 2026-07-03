using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsGhostAdapterParserTests
{
    // 実機の `pnputil /enum-devices /disconnected /class Net /format xml` 出力から採取したサンプル
    // (2026-07-03、Windows 10.0.26200)。WAN Miniport は Windows 標準の VPN 機能が使う仮想デバイスで、
    // Manufacturer=Microsoft のため IsLikelyMicrosoftVirtualDevice=true と判定されるべき。
    private const string SampleXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <PnpUtil Version="10.0.26200" Command="/enum-devices /disconnected /class Net /format xml">
            <Device InstanceId="SWD\MSRRAS\MS_PPPOEMINIPORT">
                <DeviceDescription>WAN Miniport (PPPOE)</DeviceDescription>
                <ClassName>Net</ClassName>
                <ClassGuid>{4d36e972-e325-11ce-bfc1-08002be10318}</ClassGuid>
                <ManufacturerName>Microsoft</ManufacturerName>
                <Status>Disconnected</Status>
                <DriverName>netrasa.inf</DriverName>
            </Device>
            <Device InstanceId="USB\VID_0B95&amp;PID_1790\000123456789">
                <DeviceDescription>ASIX AX88179 USB 3.0 to Gigabit Ethernet Adapter</DeviceDescription>
                <ClassName>Net</ClassName>
                <ClassGuid>{4d36e972-e325-11ce-bfc1-08002be10318}</ClassGuid>
                <ManufacturerName>ASIX Electronics Corporation</ManufacturerName>
                <Status>Disconnected</Status>
                <DriverName>ax88179.inf</DriverName>
            </Device>
        </PnpUtil>
        """;

    [TestMethod]
    public void Parse_ExtractsAllDevices()
    {
        var result = WindowsGhostAdapterParser.Parse(SampleXml);

        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public void Parse_MicrosoftWanMiniport_IsFlaggedAsLikelyVirtualDevice()
    {
        var result = WindowsGhostAdapterParser.Parse(SampleXml);
        var wanMiniport = result.Single(d => d.InstanceId == "SWD\\MSRRAS\\MS_PPPOEMINIPORT");

        Assert.AreEqual("WAN Miniport (PPPOE)", wanMiniport.Description);
        Assert.AreEqual("Microsoft", wanMiniport.Manufacturer);
        Assert.AreEqual("netrasa.inf", wanMiniport.DriverName);
        Assert.IsTrue(wanMiniport.IsLikelyMicrosoftVirtualDevice);
    }

    [TestMethod]
    public void Parse_ThirdPartyUsbAdapter_IsNotFlaggedAsMicrosoftVirtualDevice()
    {
        var result = WindowsGhostAdapterParser.Parse(SampleXml);
        var usbAdapter = result.Single(d => d.InstanceId.StartsWith("USB\\VID_0B95"));

        Assert.AreEqual("ASIX Electronics Corporation", usbAdapter.Manufacturer);
        Assert.IsFalse(usbAdapter.IsLikelyMicrosoftVirtualDevice);
    }

    [TestMethod]
    public void Parse_EmptyString_ReturnsEmptyList()
    {
        Assert.AreEqual(0, WindowsGhostAdapterParser.Parse(string.Empty).Count);
    }

    [TestMethod]
    public void Parse_MalformedXml_ReturnsEmptyListInsteadOfThrowing()
    {
        var result = WindowsGhostAdapterParser.Parse("not xml at all <<<");

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Parse_NoDeviceElements_ReturnsEmptyList()
    {
        const string emptyResultXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <PnpUtil Version="10.0.26200" Command="/enum-devices /disconnected /class Net /format xml" />
            """;

        var result = WindowsGhostAdapterParser.Parse(emptyResultXml);

        Assert.AreEqual(0, result.Count);
    }
}
