using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public sealed class WindowsGamingNetworkProfilePolicyTests
{
    [TestMethod]
    public void GetApplicableKeywords_PhysicalEthernet_UsesOnlyStandardEthernetProperties()
    {
        var keywords = WindowsGamingNetworkProfilePolicy.GetApplicableKeywords(
            true, 6, "Realtek", "PCI\\VEN_10EC&DEV_8125");

        CollectionAssert.AreEqual(
            new[] { "*InterruptModeration", "*EEE" },
            keywords.ToArray());
    }

    [TestMethod]
    public void GetApplicableKeywords_VerifiedMediaTekWifi_AddsOnlyMeasuredPowerProperties()
    {
        var keywords = WindowsGamingNetworkProfilePolicy.GetApplicableKeywords(
            true, 71, "MediaTek, Inc.", "PCI\\VEN_14C3&DEV_0616&SUBSYS_061614C3");

        CollectionAssert.AreEqual(
            new[] { "*InterruptModeration", "LowPowerEnable", "UAPSDSupport" },
            keywords.ToArray());
        Assert.DoesNotContain("*EEE", keywords);
    }

    [TestMethod]
    public void GetApplicableKeywords_UnknownWifi_DoesNotGuessVendorProperties()
    {
        var keywords = WindowsGamingNetworkProfilePolicy.GetApplicableKeywords(
            true, 71, "Unknown Vendor", "PCI\\VEN_9999&DEV_0001");

        CollectionAssert.AreEqual(new[] { "*InterruptModeration" }, keywords.ToArray());
    }

    [TestMethod]
    [DataRow(false, 71)]
    [DataRow(true, 53)]
    public void GetApplicableKeywords_UnsupportedAdapter_IsEmpty(bool hardware, int interfaceType)
    {
        Assert.IsEmpty(WindowsGamingNetworkProfilePolicy.GetApplicableKeywords(
            hardware, interfaceType, "MediaTek, Inc.", "PCI\\VEN_14C3&DEV_0616"));
    }
}
