using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsMtuStateParserTests
{
    [TestMethod]
    public void Parse_ValidMtuLine_ReturnsValue()
    {
        Assert.AreEqual(1500, WindowsMtuStateParser.Parse("MTU=1500"));
    }

    [TestMethod]
    public void Parse_JumboMtuLine_ReturnsValue()
    {
        Assert.AreEqual(9000, WindowsMtuStateParser.Parse("MTU=9000"));
    }

    [TestMethod]
    public void Parse_EmptyOutput_ReturnsNull()
    {
        Assert.IsNull(WindowsMtuStateParser.Parse(string.Empty));
    }

    [TestMethod]
    public void Parse_GarbageValue_ReturnsNull()
    {
        Assert.IsNull(WindowsMtuStateParser.Parse("MTU=not-a-number"));
    }
}
