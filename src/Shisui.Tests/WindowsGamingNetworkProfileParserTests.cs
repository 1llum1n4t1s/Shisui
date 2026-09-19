using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public sealed class WindowsGamingNetworkProfileParserTests
{
    [TestMethod]
    public void TryParse_ValidPhysicalEthernet_ParsesIdentityAndProperties()
    {
        var success = WindowsGamingNetworkProfileParser.TryParse(
            Json(Properties(Property("*InterruptModeration", "1"), Property("*EEE", "0"))),
            out var state,
            out var error);

        Assert.IsTrue(success, error);
        Assert.IsNotNull(state);
        Assert.AreEqual("Ethernet", state.AdapterName);
        Assert.IsTrue(state.HardwareInterface);
        Assert.AreEqual(6, state.InterfaceType);
        Assert.HasCount(2, state.Properties);
    }

    [TestMethod]
    public void TryParse_ValidMediaTekWifi_ParsesVendorIdentityAndProperties()
    {
        var success = WindowsGamingNetworkProfileParser.TryParse(
            State(
                interfaceType: 71,
                description: "RZ616 Wi-Fi 6E 160MHz",
                driverProvider: "MediaTek, Inc.",
                pnpDeviceId: "PCI\\VEN_14C3&DEV_0616&SUBSYS_061614C3",
                properties: [("*InterruptModeration", "1"), ("LowPowerEnable", "1"), ("UAPSDSupport", "0")]),
            out var state,
            out var error);

        Assert.IsTrue(success, error);
        Assert.IsNotNull(state);
        Assert.AreEqual(71, state.InterfaceType);
        Assert.AreEqual("MediaTek, Inc.", state.DriverProvider);
        StringAssert.StartsWith(state.PnpDeviceId, "PCI\\VEN_14C3&");
        Assert.HasCount(3, state.Properties);
    }

    [TestMethod]
    public void TryParse_MissingProperty_IsAcceptedAsUnsupported()
    {
        var success = WindowsGamingNetworkProfileParser.TryParse(
            Json(Properties(Property("*EEE", "1"))),
            out var state,
            out var error);

        Assert.IsTrue(success, error);
        Assert.IsNotNull(state);
        Assert.HasCount(1, state.Properties);
    }

    [TestMethod]
    public void TryParse_UnknownProperty_IsRejected()
    {
        Assert.IsFalse(WindowsGamingNetworkProfileParser.TryParse(
            Json(Properties(Property("*FlowControl", "1"))), out _, out var error));
        StringAssert.Contains(error, "対象外");
    }

    [TestMethod]
    public void TryParse_DuplicateProperty_IsRejected()
    {
        Assert.IsFalse(WindowsGamingNetworkProfileParser.TryParse(
            Json(Properties(Property("*EEE", "1"), Property("*EEE", "1"))), out _, out var error));
        StringAssert.Contains(error, "重複");
    }

    [TestMethod]
    [DataRow("[\"2\"]", "[\"0\",\"1\"]")]
    [DataRow("[\"0\",\"1\"]", "[\"0\",\"1\"]")]
    [DataRow("[\"1\"]", "[\"1\"]")]
    public void TryParse_InvalidCurrentOrAdvertisedValues_IsRejected(string current, string valid)
    {
        var property = $"{{\"RegistryKeyword\":\"*EEE\",\"RegistryValues\":{current},\"ValidRegistryValues\":{valid}}}";
        Assert.IsFalse(WindowsGamingNetworkProfileParser.TryParse(
            Json(Properties(property)), out _, out var error));
        StringAssert.Contains(error, "不正");
    }

    [TestMethod]
    public void TryParse_DuplicateJsonField_IsRejected()
    {
        var json = Json(Properties(Property("*EEE", "1")))
            .Replace("\"InterfaceType\":6", "\"InterfaceType\":6,\"InterfaceType\":6", StringComparison.Ordinal);
        Assert.IsFalse(WindowsGamingNetworkProfileParser.TryParse(json, out _, out var error));
        StringAssert.Contains(error, "重複");
    }

    [TestMethod]
    public void TryParse_NonNumericInterfaceType_IsRejectedWithoutThrowing()
    {
        var json = Json(Properties(Property("*EEE", "1")))
            .Replace("\"InterfaceType\":6", "\"InterfaceType\":\"Ethernet\"", StringComparison.Ordinal);

        var success = WindowsGamingNetworkProfileParser.TryParse(json, out _, out var error);

        Assert.IsFalse(success);
        StringAssert.Contains(error, "識別情報");
    }

    [TestMethod]
    [DataRow(71, "MediaTek, Inc.", "PCI\\VEN_14C3&DEV_0616", "*EEE")]
    [DataRow(6, "MediaTek, Inc.", "PCI\\VEN_14C3&DEV_0616", "LowPowerEnable")]
    [DataRow(71, "Intel", "PCI\\VEN_8086&DEV_1234", "LowPowerEnable")]
    [DataRow(71, "MediaTek, Inc.", "USB\\VID_14C3&PID_0616", "UAPSDSupport")]
    public void TryParse_CrossMediaOrProviderProperty_IsRejected(
        int interfaceType,
        string driverProvider,
        string pnpDeviceId,
        string keyword)
    {
        var json = State(
            interfaceType: interfaceType,
            driverProvider: driverProvider,
            pnpDeviceId: pnpDeviceId,
            properties: [(keyword, "1")]);

        Assert.IsFalse(WindowsGamingNetworkProfileParser.TryParse(json, out _, out var error));
        StringAssert.Contains(error, "対象外");
    }

    internal static string State(
        bool hardwareInterface = true,
        int interfaceType = 6,
        string description = "Intel Ethernet Controller",
        string guid = "fb283a95-51d8-466b-b72f-c8d27361ca9b",
        string driverProvider = "Intel",
        string pnpDeviceId = "PCI\\VEN_8086&DEV_1234",
        params (string Keyword, string Value)[] properties) =>
        Json(Properties(properties.Select(property => Property(property.Keyword, property.Value)).ToArray()),
            hardwareInterface, interfaceType, description, guid, driverProvider, pnpDeviceId);

    private static string Json(
        string properties,
        bool hardwareInterface = true,
        int interfaceType = 6,
        string description = "Intel Ethernet Controller",
        string guid = "fb283a95-51d8-466b-b72f-c8d27361ca9b",
        string driverProvider = "Intel",
        string pnpDeviceId = "PCI\\VEN_8086&DEV_1234") =>
        $"{{\"AdapterName\":\"Ethernet\",\"InterfaceGuid\":\"{guid}\",\"InterfaceDescription\":\"{description}\"," +
        $"\"HardwareInterface\":{hardwareInterface.ToString().ToLowerInvariant()},\"InterfaceType\":{interfaceType}," +
        $"\"DriverProvider\":\"{driverProvider}\",\"PnPDeviceId\":\"{pnpDeviceId.Replace("\\", "\\\\", StringComparison.Ordinal)}\"," +
        $"\"Properties\":{properties}}}";

    private static string Properties(params string[] values) => $"[{string.Join(',', values)}]";

    private static string Property(string keyword, string current) =>
        $"{{\"RegistryKeyword\":\"{keyword}\",\"RegistryValues\":[\"{current}\"],\"ValidRegistryValues\":[\"0\",\"1\"]}}";
}
