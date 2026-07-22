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
}
