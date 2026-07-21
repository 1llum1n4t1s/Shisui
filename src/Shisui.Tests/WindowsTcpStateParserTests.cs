using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsTcpStateParserTests
{
    // 実機 (Windows 10.0.26200、2026-07-03) の PowerShell 状態取得コマンド出力から採取。
    private const string AllBbr2Sample = """
        RSS=Enabled
        RSC=Enabled
        ECN=Enabled
        TIMESTAMPS=Enabled
        FASTOPEN=
        CC=BBR2
        CC=BBR2
        CC=BBR2
        CC=BBR2
        CC=BBR2
        """;

    [TestMethod]
    public void Parse_AllTemplatesBbr2_ReturnsEnabled()
    {
        var snapshot = WindowsTcpStateParser.Parse(AllBbr2Sample);
        Assert.AreEqual(Bbr2Status.Enabled, snapshot.Bbr2);
    }

    [TestMethod]
    public void Parse_ReadsGlobalOptionValues()
    {
        var snapshot = WindowsTcpStateParser.Parse(AllBbr2Sample);

        Assert.AreEqual("Enabled", snapshot.GetOptionValue(TcpGlobalOption.Rss));
        Assert.AreEqual("Enabled", snapshot.GetOptionValue(TcpGlobalOption.Rsc));
        Assert.AreEqual("Enabled", snapshot.GetOptionValue(TcpGlobalOption.EcnCapability));
        Assert.AreEqual("Enabled", snapshot.GetOptionValue(TcpGlobalOption.Timestamps));
    }

    [TestMethod]
    public void Parse_FastOpenEmpty_ReturnsEmptyString()
    {
        var snapshot = WindowsTcpStateParser.Parse(AllBbr2Sample);
        Assert.AreEqual(string.Empty, snapshot.GetOptionValue(TcpGlobalOption.FastOpen));
    }

    [TestMethod]
    public void Parse_NoTemplatesBbr2_ReturnsDisabled()
    {
        const string sample = """
            RSS=Disabled
            CC=CUBIC
            CC=CUBIC
            CC=CUBIC
            CC=CUBIC
            CC=CUBIC
            """;

        var snapshot = WindowsTcpStateParser.Parse(sample);

        Assert.AreEqual(Bbr2Status.Disabled, snapshot.Bbr2);
        Assert.AreEqual("Disabled", snapshot.GetOptionValue(TcpGlobalOption.Rss));
    }

    [TestMethod]
    public void Parse_MixedProviders_ReturnsPartial()
    {
        const string sample = """
            CC=BBR2
            CC=BBR2
            CC=CUBIC
            CC=CUBIC
            CC=CUBIC
            """;

        Assert.AreEqual(Bbr2Status.Partial, WindowsTcpStateParser.Parse(sample).Bbr2);
    }

    [TestMethod]
    public void Parse_NoProviderLines_ReturnsUnknownBbr2()
    {
        const string sample = "RSS=Enabled";
        Assert.AreEqual(Bbr2Status.Unknown, WindowsTcpStateParser.Parse(sample).Bbr2);
    }

    [TestMethod]
    public void Parse_TimestampsAllowed_PreservesRawValue()
    {
        // Timestamps は Enabled/Disabled 以外に "Allowed" もありうる。生値をそのまま保持する。
        var snapshot = WindowsTcpStateParser.Parse("TIMESTAMPS=Allowed");
        Assert.AreEqual("Allowed", snapshot.GetOptionValue(TcpGlobalOption.Timestamps));
    }

    [TestMethod]
    public void Parse_EmptyOrGarbage_DoesNotThrow_AndIsUnknown()
    {
        var snapshot = WindowsTcpStateParser.Parse("これは想定外の出力\n\n===");
        Assert.AreEqual(Bbr2Status.Unknown, snapshot.Bbr2);
        Assert.AreEqual(string.Empty, snapshot.GetOptionValue(TcpGlobalOption.Rss));
    }

    [TestMethod]
    public void Parse_HandlesCarriageReturnsFromWindowsOutput()
    {
        // PowerShell の実出力は CRLF。\r が値に混ざらず Trim されることを確認。
        const string sample = "RSS=Enabled\r\nCC=BBR2\r\nCC=BBR2\r\nCC=BBR2\r\nCC=BBR2\r\nCC=BBR2\r\n";
        var snapshot = WindowsTcpStateParser.Parse(sample);

        Assert.AreEqual("Enabled", snapshot.GetOptionValue(TcpGlobalOption.Rss));
        Assert.AreEqual(Bbr2Status.Enabled, snapshot.Bbr2);
    }

    [TestMethod]
    public void Parse_ReadsAutoTuningLevel()
    {
        var snapshot = WindowsTcpStateParser.Parse(AllBbr2Sample + "\nAUTOTUNE=Normal");
        Assert.AreEqual("Normal", snapshot.AutoTuningLevel);
    }

    [TestMethod]
    public void Parse_NamedProviders_PreservesEachTemplateForRestore()
    {
        const string sample = """
            CC=Internet|CUBIC
            CC=InternetCustom|BBR2
            CC=Datacenter|DCTCP
            CC=DatacenterCustom|CUBIC
            CC=Compat|NewReno
            """;

        var providers = WindowsTcpStateParser.Parse(sample).GetCongestionProviders();

        Assert.HasCount(5, providers);
        Assert.AreEqual("CUBIC", providers["Internet"]);
        Assert.AreEqual("BBR2", providers["InternetCustom"]);
        Assert.AreEqual(Bbr2Status.Partial, WindowsTcpStateParser.Parse(sample).Bbr2);
    }

    [TestMethod]
    public void Parse_NoAutoTuneLine_ReturnsEmptyString()
    {
        var snapshot = WindowsTcpStateParser.Parse(AllBbr2Sample);
        Assert.AreEqual(string.Empty, snapshot.AutoTuningLevel);
    }
}
