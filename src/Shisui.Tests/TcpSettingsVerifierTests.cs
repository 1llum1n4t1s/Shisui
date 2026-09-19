using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services;

namespace Shisui.Tests;

[TestClass]
public class TcpSettingsVerifierTests
{
    [TestMethod]
    public void VerifyBbr2Enabled_AllTemplatesEnabled_ReturnsSuccess()
    {
        var result = TcpSettingsVerifier.VerifyBbr2Enabled(CreateSnapshot(Bbr2Status.Enabled));

        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, result.ExitCode);
        StringAssert.Contains(result.StandardOutput, "Internet=BBR2");
    }

    [TestMethod]
    public void VerifyBbr2Enabled_MismatchedTemplate_ReturnsFailure()
    {
        var result = TcpSettingsVerifier.VerifyBbr2Enabled(CreateSnapshot(Bbr2Status.Partial));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(-1, result.ExitCode);
        StringAssert.Contains(result.StandardError, "すべてが BBR2 ではありません");
    }

    [TestMethod]
    public void VerifyBbr2Enabled_UnknownState_ReturnsFailureWithoutGuessing()
    {
        var result = TcpSettingsVerifier.VerifyBbr2Enabled(CreateSnapshot(Bbr2Status.Unknown));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.StandardError, "確認できませんでした");
    }

    [TestMethod]
    public void VerifyAutoTuning_LocalValueMatches_ReturnsSuccess()
    {
        var snapshot = CreateSnapshot(
            autoTuningLevel: "Normal",
            autoTuningLevelGroupPolicy: "Disabled",
            autoTuningLevelEffective: "Local");

        var result = TcpSettingsVerifier.VerifyAutoTuning(snapshot, AutoTuningLevel.Normal);

        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.StandardOutput, "Effective=Normal");
    }

    [TestMethod]
    public void VerifyAutoTuning_GroupPolicyOverridesLocal_UsesPolicyValue()
    {
        var snapshot = CreateSnapshot(
            autoTuningLevel: "Normal",
            autoTuningLevelGroupPolicy: "Disabled",
            autoTuningLevelEffective: "GroupPolicy");

        var normalResult = TcpSettingsVerifier.VerifyAutoTuning(snapshot, AutoTuningLevel.Normal);
        var disabledResult = TcpSettingsVerifier.VerifyAutoTuning(snapshot, AutoTuningLevel.Disabled);

        Assert.IsFalse(normalResult.Success);
        Assert.IsTrue(disabledResult.Success);
        StringAssert.Contains(normalResult.StandardOutput, "Effective=Disabled");
    }

    [TestMethod]
    public void VerifyAutoTuning_UnknownEffectiveSource_ReturnsFailureWithoutLocalFallback()
    {
        var snapshot = CreateSnapshot(
            autoTuningLevel: "Normal",
            autoTuningLevelGroupPolicy: "Disabled",
            autoTuningLevelEffective: "UnknownSource");

        var result = TcpSettingsVerifier.VerifyAutoTuning(snapshot, AutoTuningLevel.Normal);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.StandardError, "確認できませんでした");
    }

    private static TcpSettingsSnapshot CreateSnapshot(
        Bbr2Status bbr2 = Bbr2Status.Enabled,
        string autoTuningLevel = "Normal",
        string autoTuningLevelGroupPolicy = "",
        string autoTuningLevelEffective = "Local") =>
        new(
            bbr2,
            new Dictionary<TcpGlobalOption, string>(),
            autoTuningLevel,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Internet"] = "BBR2",
                ["InternetCustom"] = "BBR2",
                ["Datacenter"] = "BBR2",
                ["DatacenterCustom"] = "BBR2",
                ["Compat"] = bbr2 == Bbr2Status.Partial ? "CUBIC" : "BBR2",
            },
            autoTuningLevelGroupPolicy,
            autoTuningLevelEffective);
}
