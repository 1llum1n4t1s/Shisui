using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Interfaces;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsTcpCommandBuilderTests
{
    [TestMethod]
    public void BuildEnableBbr2_MatchesSpecifiedCommandSet()
    {
        var commands = WindowsTcpCommandBuilder.BuildEnableBbr2();

        CollectionAssert.AreEqual(new[]
        {
            "int tcp set supplemental template=Internet congestionprovider=BBR2",
            "int tcp set supplemental template=InternetCustom congestionprovider=BBR2",
            "int tcp set supplemental template=Datacenter congestionprovider=BBR2",
            "int tcp set supplemental template=DatacenterCustom congestionprovider=BBR2",
            "int tcp set supplemental template=Compat congestionprovider=BBR2",
            "int ipv6 set global loopbacklargemtu=disable",
            "int ipv4 set global loopbacklargemtu=disable",
        }, commands.ToList());
    }

    [TestMethod]
    public void BuildRevertBbr2ToDefault_MirrorsEnableWithDefaultValues()
    {
        var commands = WindowsTcpCommandBuilder.BuildRevertBbr2ToDefault();

        CollectionAssert.AreEqual(new[]
        {
            "int tcp set supplemental template=Internet congestionprovider=default",
            "int tcp set supplemental template=InternetCustom congestionprovider=default",
            "int tcp set supplemental template=Datacenter congestionprovider=default",
            "int tcp set supplemental template=DatacenterCustom congestionprovider=default",
            "int tcp set supplemental template=Compat congestionprovider=default",
            "int ipv6 set global loopbacklargemtu=enabled",
            "int ipv4 set global loopbacklargemtu=enabled",
        }, commands.ToList());
    }

    [TestMethod]
    public void BuildSetCongestionProviders_RestoresEachTemplateExactly()
    {
        var providers = new Dictionary<string, string>
        {
            ["Internet"] = "CUBIC",
            ["InternetCustom"] = "BBR2",
            ["Datacenter"] = "DCTCP",
            ["DatacenterCustom"] = "CUBIC",
            ["Compat"] = "NewReno",
        };

        CollectionAssert.AreEqual(new[]
        {
            "int tcp set supplemental template=Internet congestionprovider=CUBIC",
            "int tcp set supplemental template=InternetCustom congestionprovider=BBR2",
            "int tcp set supplemental template=Datacenter congestionprovider=DCTCP",
            "int tcp set supplemental template=DatacenterCustom congestionprovider=CUBIC",
            "int tcp set supplemental template=Compat congestionprovider=NewReno",
        }, WindowsTcpCommandBuilder.BuildSetCongestionProviders(providers).ToList());
    }

    [TestMethod]
    public void BuildRevertGlobalOptionsToDefault_ResetsCommonTweakValuesIndividually()
    {
        var commands = WindowsTcpCommandBuilder.BuildRevertGlobalOptionsToDefault();

        CollectionAssert.AreEqual(new[]
        {
            "int tcp set global rss=default",
            "int tcp set global rsc=default",
            "int tcp set global ecncapability=default",
            "int tcp set global timestamps=allowed",
            "int tcp set global initialrto=3000",
            "int tcp set global nonsackrttresiliency=default",
            "int tcp set global maxsynretransmissions=2",
            "int tcp set global fastopen=default",
            "int tcp set global fastopenfallback=default",
            "int tcp set global hystart=default",
            "int tcp set global prr=default",
            "int tcp set global pacingprofile=default",
            "int tcp set heuristics forcews=default",
        }, commands.ToList());
    }

    [TestMethod]
    [DataRow(TcpGlobalOption.Rsc, true, "int tcp set global rsc=enabled")]
    [DataRow(TcpGlobalOption.Rsc, false, "int tcp set global rsc=disabled")]
    [DataRow(TcpGlobalOption.EcnCapability, true, "int tcp set global ecncapability=enabled")]
    [DataRow(TcpGlobalOption.Timestamps, false, "int tcp set global timestamps=disabled")]
    [DataRow(TcpGlobalOption.Rss, true, "int tcp set global rss=enabled")]
    [DataRow(TcpGlobalOption.FastOpen, true, "int tcp set global fastopen=enabled")]
    public void BuildSetGlobalOption_ProducesExpectedCommand(TcpGlobalOption option, bool enabled, string expected)
    {
        Assert.AreEqual(expected, WindowsTcpCommandBuilder.BuildSetGlobalOption(option, enabled));
    }

    [TestMethod]
    [DataRow(TcpGlobalOption.Rsc, "int tcp set global rsc=default")]
    [DataRow(TcpGlobalOption.EcnCapability, "int tcp set global ecncapability=default")]
    [DataRow(TcpGlobalOption.Timestamps, "int tcp set global timestamps=allowed")]
    [DataRow(TcpGlobalOption.Rss, "int tcp set global rss=default")]
    [DataRow(TcpGlobalOption.FastOpen, "int tcp set global fastopen=default")]
    public void BuildRevertGlobalOptionToDefault_ProducesWindowsDefaultCommand(
        TcpGlobalOption option,
        string expected)
    {
        Assert.AreEqual(expected, WindowsTcpCommandBuilder.BuildRevertGlobalOptionToDefault(option));
    }

    [TestMethod]
    [DataRow(AutoTuningLevel.Disabled, "int tcp set global autotuninglevel=disabled")]
    [DataRow(AutoTuningLevel.HighlyRestricted, "int tcp set global autotuninglevel=highlyrestricted")]
    [DataRow(AutoTuningLevel.Restricted, "int tcp set global autotuninglevel=restricted")]
    [DataRow(AutoTuningLevel.Normal, "int tcp set global autotuninglevel=normal")]
    [DataRow(AutoTuningLevel.Experimental, "int tcp set global autotuninglevel=experimental")]
    public void BuildSetAutoTuningLevel_ProducesExpectedCommand(AutoTuningLevel level, string expected)
    {
        Assert.AreEqual(expected, WindowsTcpCommandBuilder.BuildSetAutoTuningLevel(level));
    }

    [TestMethod]
    public void BuildRevertMtuToDefault_QuotesAdapterName_AndSetsBothIpv4AndIpv6To1500()
    {
        var commands = WindowsTcpCommandBuilder.BuildRevertMtuToDefault("Wi-Fi 2");

        CollectionAssert.AreEqual(new[]
        {
            "interface ipv4 set subinterface name=\"Wi-Fi 2\" mtu=1500 store=persistent",
            "interface ipv6 set subinterface name=\"Wi-Fi 2\" mtu=1500 store=persistent",
        }, commands.ToList());
    }
}
