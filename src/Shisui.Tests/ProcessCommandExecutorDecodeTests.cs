using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services;

namespace Shisui.Tests;

/// <summary>
/// <see cref="ProcessCommandExecutor.DecodeConsoleOutput"/> のエンコーディング自動判別を検証する。
/// netsh/ipconfig の出力が UTF-8 でも OEM コードページ (日本語 = CP932) でも正しく読めることを確認する。
/// CP932 ケースは実行時に CodePages プロバイダが利用可能であることの検証も兼ねる (.NET 10 標準フレームワーク)。
/// </summary>
[TestClass]
public class ProcessCommandExecutorDecodeTests
{
    [ClassInitialize]
    public static void Init(TestContext _) =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [TestMethod]
    public void Decode_Utf8JapaneseBytes_ReturnsCorrectText()
    {
        const string text = "アクティブ状態を照会しています";
        var bytes = new UTF8Encoding(false).GetBytes(text);
        Assert.AreEqual(text, ProcessCommandExecutor.DecodeConsoleOutput(bytes));
    }

    [TestMethod]
    public void Decode_Cp932JapaneseBytes_FallsBackAndReturnsCorrectText()
    {
        const string text = "受信ウィンドウ自動チューニング レベル";
        var bytes = Encoding.GetEncoding(932).GetBytes(text);
        // CP932 の日本語バイト列は厳密 UTF-8 として不正 → OEM フォールバックで正しく復元される。
        Assert.AreEqual(text, ProcessCommandExecutor.DecodeConsoleOutput(bytes));
    }

    [TestMethod]
    public void Decode_AsciiOnly_ReturnsSameText()
    {
        const string text = "Receive-Side Scaling : enabled";
        var bytes = Encoding.ASCII.GetBytes(text);
        Assert.AreEqual(text, ProcessCommandExecutor.DecodeConsoleOutput(bytes));
    }

    [TestMethod]
    public void Decode_Empty_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, ProcessCommandExecutor.DecodeConsoleOutput([]));
    }
}
