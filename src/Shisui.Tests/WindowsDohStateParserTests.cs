using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Models;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsDohStateParserTests
{
    [TestMethod]
    public void Parse_AllExpectedAddressesAutoUpgradeTrue_ReturnsEnabled()
    {
        const string sample = """
            SERVER=1.1.1.1
            AUTOUPGRADE=True
            SERVER=1.0.0.1
            AUTOUPGRADE=True
            """;

        var status = WindowsDohStateParser.Parse(sample, ["1.1.1.1", "1.0.0.1"]);
        Assert.AreEqual(DohStatus.Enabled, status);
    }

    [TestMethod]
    public void Parse_NoMatchingOutput_ReturnsDisabled()
    {
        var status = WindowsDohStateParser.Parse(string.Empty, ["1.1.1.1", "1.0.0.1"]);
        Assert.AreEqual(DohStatus.Disabled, status);
    }

    [TestMethod]
    public void Parse_OneOfTwoAddressesRegistered_ReturnsPartial()
    {
        const string sample = """
            SERVER=1.1.1.1
            AUTOUPGRADE=True
            """;

        var status = WindowsDohStateParser.Parse(sample, ["1.1.1.1", "1.0.0.1"]);
        Assert.AreEqual(DohStatus.Partial, status);
    }

    [TestMethod]
    public void Parse_AutoUpgradeFalse_DoesNotCountAsEnabled()
    {
        const string sample = """
            SERVER=1.1.1.1
            AUTOUPGRADE=False
            """;

        var status = WindowsDohStateParser.Parse(sample, ["1.1.1.1"]);
        Assert.AreEqual(DohStatus.Disabled, status);
    }

    [TestMethod]
    public void Parse_NoExpectedAddresses_ReturnsUnknown()
    {
        var status = WindowsDohStateParser.Parse("SERVER=1.1.1.1\nAUTOUPGRADE=True", []);
        Assert.AreEqual(DohStatus.Unknown, status);
    }

    [TestMethod]
    public void Parse_HandlesCarriageReturnsFromWindowsOutput()
    {
        const string sample = "SERVER=1.1.1.1\r\nAUTOUPGRADE=True\r\n";
        var status = WindowsDohStateParser.Parse(sample, ["1.1.1.1"]);
        Assert.AreEqual(DohStatus.Enabled, status);
    }

    [TestMethod]
    public void Parse_IsCaseInsensitiveForAddressMatching()
    {
        // IPv6 は表記ゆれ (大文字/小文字) がありうる。
        const string sample = """
            SERVER=2606:4700:4700::1111
            AUTOUPGRADE=True
            """;

        var status = WindowsDohStateParser.Parse(sample, ["2606:4700:4700::1111"]);
        Assert.AreEqual(DohStatus.Enabled, status);
    }

    [TestMethod]
    public void Parse_GarbageOutput_DoesNotThrow_AndIsDisabled()
    {
        var status = WindowsDohStateParser.Parse("これは想定外の出力\n\n===", ["1.1.1.1"]);
        Assert.AreEqual(DohStatus.Disabled, status);
    }
}
