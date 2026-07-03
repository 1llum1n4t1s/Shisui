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
}
