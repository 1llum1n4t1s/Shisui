using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsGhostAdapterCommandBuilderTests
{
    [TestMethod]
    public void ListArguments_TargetsDisconnectedNetDevicesAsXml()
    {
        var args = WindowsGhostAdapterCommandBuilder.ListArguments;

        // 切断済み (= ゴースト) のネットワーククラスデバイスだけを対象にする。
        Assert.IsTrue(args.Contains("/enum-devices"), args);
        Assert.IsTrue(args.Contains("/disconnected"), args);
        Assert.IsTrue(args.Contains("/class Net"), args);
        // 出力ラベルが OS 表示言語で変わらないよう、パースは必ず XML 形式で行う。
        Assert.IsTrue(args.Contains("/format xml"), args);
    }

    [TestMethod]
    public void BuildRemove_QuotesInstanceId()
    {
        var result = WindowsGhostAdapterCommandBuilder.BuildRemove("USB\\VID_0B95&PID_1790\\000123456789");

        Assert.AreEqual("/remove-device \"USB\\VID_0B95&PID_1790\\000123456789\"", result);
    }
}
