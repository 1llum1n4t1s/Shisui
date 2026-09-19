using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsTcpStateCommandBuilderTests
{
    [TestMethod]
    public void Arguments_StopsOnPartialPowerShellFailure()
    {
        StringAssert.Contains(WindowsTcpStateCommandBuilder.Arguments, "$ErrorActionPreference='Stop';");
    }

    [TestMethod]
    public void Arguments_EmitsAutoTuningPolicyAndEffectiveSource()
    {
        StringAssert.Contains(
            WindowsTcpStateCommandBuilder.Arguments,
            "'AUTOTUNE_POLICY='+$t.AutoTuningLevelGroupPolicy;");
        StringAssert.Contains(
            WindowsTcpStateCommandBuilder.Arguments,
            "'AUTOTUNE_SOURCE='+$t.AutoTuningLevelEffective;");
    }
}
