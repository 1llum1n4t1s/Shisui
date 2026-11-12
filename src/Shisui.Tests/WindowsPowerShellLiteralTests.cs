using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

/// <summary>
/// <see cref="WindowsPowerShellLiteral"/> が PowerShell の単一引用符リテラルを壊さないことを検証する。
/// PowerShell は ASCII の ' に加えて U+2018 / U+2019 / U+201A / U+201B も単一引用符として扱う
/// (Windows PowerShell 5.1 と PowerShell 7 の両方で、' だけを二重化した値に ’ を混ぜると
/// リテラルが閉じて後続がコードとして評価されることを確認済み)。
/// </summary>
[TestClass]
public class WindowsPowerShellLiteralTests
{
    [TestMethod]
    [DataRow("Ethernet", "Ethernet")]
    [DataRow("", "")]
    [DataRow("a'b", "a''b")]
    [DataRow("a‘b", "a‘‘b")]
    [DataRow("a’b", "a’’b")]
    [DataRow("a‚b", "a‚‚b")]
    [DataRow("a‛b", "a‛‛b")]
    [DataRow("'’'", "''’’''")]
    [DataRow("イーサネット 2", "イーサネット 2")]
    public void Escape_DoublesEverySingleQuoteVariantWithItself(string value, string expected)
    {
        Assert.AreEqual(expected, WindowsPowerShellLiteral.Escape(value));
    }

    [TestMethod]
    public void Escape_ValueWithoutQuotes_ReturnsSameInstance()
    {
        // 引用符を含まない値では生成文字列が共通化前と完全に一致することを保証する。
        const string value = "Wi-Fi";
        Assert.AreSame(value, WindowsPowerShellLiteral.Escape(value));
    }

    [TestMethod]
    public void Escape_LeavesDoubleQuoteVariantsUntouched()
    {
        // 単一引用符リテラル内では二重引用符系は区切りにならないため加工しない
        // (外側 -Command "..." を破る ASCII の " は呼び出し側で拒否する)。
        const string value = "a\"b“c”d";
        Assert.AreEqual(value, WindowsPowerShellLiteral.Escape(value));
    }

    [TestMethod]
    public void Quote_WrapsEscapedValue()
    {
        Assert.AreEqual("'a’’b'", WindowsPowerShellLiteral.Quote("a’b"));
    }

    [TestMethod]
    public void Escape_Null_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => WindowsPowerShellLiteral.Escape(null!));
    }
}
