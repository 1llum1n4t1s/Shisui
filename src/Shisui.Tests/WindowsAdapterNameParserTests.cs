using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsAdapterNameParserTests
{
    [TestMethod]
    public void Parse_DecodesUnicodeNames()
    {
        var output = Record("ローカル エリア接続 3")
                     + Record("6to4 Adapter");

        var records = WindowsAdapterNameParser.Parse(output);

        Assert.HasCount(2, records);
        Assert.AreEqual("ローカル エリア接続 3", records[0].Name);
        Assert.AreEqual("6to4 Adapter", records[1].Name);
    }

    [TestMethod]
    public void Parse_MalformedRecord_IsSkipped()
    {
        var records = WindowsAdapterNameParser.Parse("BEGIN\nNAME=not-base64\nEND\n");

        Assert.IsEmpty(records);
    }

    internal static string Record(string name) =>
        $"BEGIN\nNAME={Encode(name)}\nEND\n";

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}
