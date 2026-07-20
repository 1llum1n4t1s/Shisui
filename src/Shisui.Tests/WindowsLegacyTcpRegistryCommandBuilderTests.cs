using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsLegacyTcpRegistryCommandBuilderTests
{
    [TestMethod]
    public void Arguments_RemovesOnlyKnownPerInterfaceAckTweaks()
    {
        var arguments = WindowsLegacyTcpRegistryCommandBuilder.Arguments;

        Assert.StartsWith("-NoProfile -NonInteractive -Command \"", arguments);
        StringAssert.Contains(arguments, "HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces");
        StringAssert.Contains(arguments, "@('TcpAckFrequency','TCPNoDelay','TcpDelAckTicks')");
        StringAssert.Contains(arguments, "Remove-ItemProperty -LiteralPath $key.PSPath -Name $name");
        StringAssert.Contains(arguments, "'REMOVED='+$removed");
        Assert.DoesNotContain("Remove-Item ", arguments, "インターフェースキー自体を削除してはいけません");
        Assert.DoesNotContain("MSMQ", arguments, "Message Queuing 固有の TCPNoDelay は対象外にするべきです");
    }
}
