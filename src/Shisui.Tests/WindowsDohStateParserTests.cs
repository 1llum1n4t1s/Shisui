using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Models;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsDohStateParserTests
{
    private const string Template = "https://cloudflare-dns.com/dns-query";
    private static string Sample(string address, bool upgrade = true) =>
        "{\"ServerAddress\":\"" + address + "\",\"DohTemplate\":\"" + Template + "\",\"AutoUpgrade\":" + (upgrade ? "true" : "false") + ",\"AllowFallbackToUdp\":false}";

    [TestMethod]
    public void Parse_AllExpectedAddressesAutoUpgradeTrue_ReturnsEnabled() =>
        Assert.AreEqual(DohStatus.Enabled, WindowsDohStateParser.Parse("[" + Sample("1.1.1.1") + "," + Sample("1.0.0.1") + "]", ["1.1.1.1", "1.0.0.1"], Template));

    [TestMethod]
    public void Parse_NoMatchingOutput_ReturnsUnknown() =>
        Assert.AreEqual(DohStatus.Unknown, WindowsDohStateParser.Parse(string.Empty, ["1.1.1.1"], Template));

    [TestMethod]
    public void Parse_OneOfTwoAddressesRegistered_ReturnsUnknown() =>
        Assert.AreEqual(DohStatus.Unknown, WindowsDohStateParser.Parse("[" + Sample("1.1.1.1") + "]", ["1.1.1.1", "1.0.0.1"], Template));

    [TestMethod]
    public void Parse_AutoUpgradeFalse_DoesNotCountAsEnabled() =>
        Assert.AreEqual(DohStatus.Disabled, WindowsDohStateParser.Parse("[" + Sample("1.1.1.1", false) + "]", ["1.1.1.1"], Template));

    [TestMethod]
    public void Parse_NoExpectedAddresses_ReturnsUnknown() =>
        Assert.AreEqual(DohStatus.Unknown, WindowsDohStateParser.Parse("[]", [], Template));

    [TestMethod]
    public void Parse_HandlesCarriageReturnsFromWindowsOutput() =>
        Assert.AreEqual(DohStatus.Enabled, WindowsDohStateParser.Parse("[" + Sample("1.1.1.1") + "]\r\n", ["1.1.1.1"], Template));

    [TestMethod]
    public void Parse_IsCaseInsensitiveForAddressMatching() =>
        Assert.AreEqual(DohStatus.Enabled, WindowsDohStateParser.Parse("[" + Sample("2606:4700:4700::AAAA") + "]", ["2606:4700:4700::aaaa"], Template));

    [TestMethod]
    public void Parse_GarbageOutput_DoesNotThrow_AndIsUnknown() =>
        Assert.AreEqual(DohStatus.Unknown, WindowsDohStateParser.Parse("これは想定外の出力", ["1.1.1.1"], Template));
}
