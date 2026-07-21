using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsAdapterNameNormalizerTests
{
    [TestMethod]
    [DataRow("ローカルエリア接続 1", "ローカルエリア接続")]
    [DataRow("ローカル エリア接続 12", "ローカル エリア接続")]
    [DataRow("Ethernet #3", "Ethernet")]
    [DataRow("Wi-Fi", "Wi-Fi")]
    public void GetBaseConnectionName_RemovesOnlyGeneratedTrailingOrdinal(string input, string expected) =>
        Assert.AreEqual(expected, WindowsAdapterNameNormalizer.GetBaseConnectionName(input));

}
