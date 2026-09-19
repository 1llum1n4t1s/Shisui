using System.Runtime.Versioning;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class ExecutableTrustVerifierTests
{
    [TestMethod]
    public void HasExpectedCommonName_MatchesOnAttributeBoundaryOnly()
    {
        Assert.IsTrue(ExecutableTrustVerifier.HasExpectedCommonName(
            "CN=Microsoft Windows, O=Microsoft Corporation, C=US",
            "Microsoft Windows"));
        Assert.IsFalse(ExecutableTrustVerifier.HasExpectedCommonName(
            "CN=Microsoft Windows Attacker, O=Example",
            "Microsoft Windows"));
    }

    [TestMethod]
    public void BuildProviderFlags_DistinguishesOnlineFromCacheOnly()
    {
        var online = ExecutableTrustVerifier.BuildProviderFlags(AuthenticodeRevocationMode.Online);
        var cacheOnly = ExecutableTrustVerifier.BuildProviderFlags(AuthenticodeRevocationMode.CacheOnly);

        Assert.IsTrue(online.HasFlag(
            ExecutableTrustVerifier.WinTrustProviderFlags.RevocationCheckChainExcludeRoot));
        Assert.IsFalse(online.HasFlag(
            ExecutableTrustVerifier.WinTrustProviderFlags.CacheOnlyUrlRetrieval));
        Assert.IsTrue(cacheOnly.HasFlag(
            ExecutableTrustVerifier.WinTrustProviderFlags.RevocationCheckChainExcludeRoot));
        Assert.IsTrue(cacheOnly.HasFlag(
            ExecutableTrustVerifier.WinTrustProviderFlags.CacheOnlyUrlRetrieval));
    }

    [TestMethod]
    public void TryVerify_MissingFile_FailsClosed()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe");

        Assert.IsFalse(ExecutableTrustVerifier.TryVerify(
            missing,
            "Microsoft Windows",
            out var reason));
        StringAssert.Contains(reason, "見つかりません");
    }

    [TestMethod]
    public void TryVerify_HeldHandle_UsesHandleWithoutReopeningForWrite()
    {
        var path = Path.Combine(Path.GetTempPath(), $"unsigned-{Guid.NewGuid():N}.exe");
        try
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            file.WriteByte(0x42);
            file.Flush(flushToDisk: true);
            file.Position = 0;

            Assert.IsFalse(ExecutableTrustVerifier.TryVerify(
                path,
                file.SafeFileHandle,
                "Microsoft Windows",
                AuthenticodeRevocationMode.CacheOnly,
                out var reason));
            Assert.IsFalse(string.IsNullOrWhiteSpace(reason));
            Assert.ThrowsExactly<IOException>(() => File.OpenWrite(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
