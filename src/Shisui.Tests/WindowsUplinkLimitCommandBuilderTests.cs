using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Models;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsUplinkLimitCommandBuilderTests
{
    private static string B(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    private static readonly string Line = $"LINE=OK,{{11111111-2222-3333-4444-555555555555}},{B("Ethernet")},Physical,AA-BB-CC-DD-EE-FF";

    [TestMethod]
    public void ParseState_ClassifiesPoliciesAndLine()
    {
        var output = $"{Line}\nQOSACTIVE={B("shisuiuplinklimit")},28000000,Default\nQOSACTIVE={B("Other")},-1,None\nQOSDEFAULT={B("Legacy")}\nDONE=1\n";

        var state = WindowsUplinkLimitCommandBuilder.ParseState(output);

        Assert.AreEqual(UplinkStateReadStatus.Ok, state.ReadStatus);
        Assert.IsTrue(state.Line.IsIdentified);
        Assert.AreEqual(UplinkAdapterKind.Physical, state.Line.Kind);
        Assert.AreEqual(28_000_000, state.LimitPolicy?.ThrottleBitsPerSecond);
        Assert.IsNull(state.TrialPolicy);
        CollectionAssert.AreEqual(new[] { "Other", "Legacy" }, state.OtherPolicyNames.ToArray());
    }

    [TestMethod]
    [DataRow("LINE=UNKNOWN\nLINE=UNKNOWN\nDONE=1\n")]
    [DataRow("LINE=UNKNOWN\n")]
    [DataRow("LINE=UNKNOWN\nUNEXPECTED=1\nDONE=1\n")]
    [DataRow("DONE=1\n")]
    [DataRow("LINE=OK,{11111111-2222-3333-4444-555555555555},RXRoZXI=,Physical,AA-BB\nDONE=1\n")]
    public void ParseState_InvalidOutput_IsUnreadable(string output) =>
        Assert.AreEqual(UplinkStateReadStatus.Unreadable, WindowsUplinkLimitCommandBuilder.ParseState(output).ReadStatus);

    [TestMethod]
    public void ParseState_DuplicatePolicy_IsUnreadable()
    {
        var duplicate = $"LINE=UNKNOWN\nQOSACTIVE={B("A")},1,Default\nQOSACTIVE={B("a")},1,Default\nDONE=1\n";

        Assert.AreEqual(UplinkStateReadStatus.Unreadable, WindowsUplinkLimitCommandBuilder.ParseState(duplicate).ReadStatus);
    }

    [TestMethod]
    public void ParseState_ForeignPolicyWithUnexpectedFields_OnlyNameIsUsed()
    {
        var output = $"{Line}\nQOSACTIVE={B("Corp")},abc,Weird\nQOSACTIVE={B("Other")},1\nDONE=1\n";

        var state = WindowsUplinkLimitCommandBuilder.ParseState(output);

        Assert.AreEqual(UplinkStateReadStatus.Ok, state.ReadStatus);
        CollectionAssert.AreEqual(new[] { "Corp", "Other" }, state.OtherPolicyNames.ToArray());
    }

    [TestMethod]
    public void ParseState_OwnPolicyWithBadTemplate_IsStillUnreadable()
    {
        var output = $"{Line}\nQOSACTIVE={B("shisuiuplinktrial")},28000000,Weird\nDONE=1\n";

        Assert.AreEqual(UplinkStateReadStatus.Unreadable, WindowsUplinkLimitCommandBuilder.ParseState(output).ReadStatus);
    }

    [TestMethod]
    public void ParsePolicyReadback_RequiresMatchingCount()
    {
        Assert.AreEqual(0, WindowsUplinkLimitCommandBuilder.ParsePolicyReadback("COUNT=0\r\n")?.Count);
        Assert.AreEqual(1, WindowsUplinkLimitCommandBuilder.ParsePolicyReadback($"POLICY={B("x")},5,Default\nCOUNT=1\n")?.Count);
        Assert.IsNull(WindowsUplinkLimitCommandBuilder.ParsePolicyReadback("COUNT=1\n"));
        Assert.IsNull(WindowsUplinkLimitCommandBuilder.ParsePolicyReadback("junk"));
    }

    [TestMethod]
    public void BuildApplyArguments_RejectsForeignNameAndOutOfRange()
    {
        Assert.ThrowsExactly<ArgumentException>(() => WindowsUplinkLimitCommandBuilder.BuildApplyArguments("Other", 5_000_000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WindowsUplinkLimitCommandBuilder.BuildApplyArguments("ShisuiUplinkLimit", 10));
        Assert.ThrowsExactly<ArgumentException>(() => WindowsUplinkLimitCommandBuilder.BuildRemoveArguments("Other"));
    }
}
