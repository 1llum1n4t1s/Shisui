using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public sealed class WindowsPowerPlanCommandBuilderTests
{
    private static readonly Guid Scheme = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");

    [TestMethod]
    public void TryParseActiveScheme_LocalizedOutput_ParsesSingleGuid()
    {
        Assert.IsTrue(WindowsPowerPlanCommandBuilder.TryParseActiveScheme(
            $"電源設定の GUID: {Scheme:D}  (バランス)", out var actual));
        Assert.AreEqual(Scheme, actual);
    }

    [TestMethod]
    public void TryParseLinkStateValues_ExpectedGuidsAndTwoValues_ParsesAcThenDc()
    {
        var output = QueryOutput(Scheme, 2, 1);

        Assert.IsTrue(WindowsPowerPlanCommandBuilder.TryParseLinkStateValues(output, Scheme, out var ac, out var dc));
        Assert.AreEqual(2U, ac);
        Assert.AreEqual(1U, dc);
    }

    [TestMethod]
    public void TryParseLinkStateValues_WrongSettingOrUnexpectedValue_Rejects()
    {
        var wrongSetting = QueryOutput(Scheme, 2, 1)
            .Replace(WindowsPowerPlanCommandBuilder.LinkStatePowerManagementGuid,
                "00000000-0000-0000-0000-000000000000", StringComparison.Ordinal);
        Assert.IsFalse(WindowsPowerPlanCommandBuilder.TryParseLinkStateValues(wrongSetting, Scheme, out _, out _));

        Assert.IsFalse(WindowsPowerPlanCommandBuilder.TryParseLinkStateValues(
            QueryOutput(Scheme, 3, 1), Scheme, out _, out _));
    }

    internal static string QueryOutput(Guid scheme, uint ac, uint dc) => $"""
        電源設定の GUID: {scheme:D}  (バランス)
          サブグループの GUID: {WindowsPowerPlanCommandBuilder.PciExpressSubgroupGuid}  (PCI Express)
            電源設定の GUID: {WindowsPowerPlanCommandBuilder.LinkStatePowerManagementGuid}  (リンク状態の電源管理)
            現在の AC 電源設定のインデックス: 0x{ac:X8}
            現在の DC 電源設定のインデックス: 0x{dc:X8}
        """;
}
