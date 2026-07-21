using System.Runtime.Versioning;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public class WindowsPerMachineMigrationTests
{
    private string testRoot = null!;

    [TestInitialize]
    public void Initialize()
    {
        testRoot = Path.Combine(Path.GetTempPath(), "Shisui.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [TestMethod]
    public void GetLegacyRootIfCurrentProcessIsPerUser_ProcessIsUnderExpectedRoot_ReturnsRoot()
    {
        var localAppData = Path.Combine(testRoot, "LocalAppData");
        var legacyRoot = Path.Combine(localAppData, "Shisui");
        var processPath = Path.Combine(legacyRoot, "current", "Shisui.UI.exe");

        var result = WindowsPerMachineMigration.GetLegacyRootIfCurrentProcessIsPerUser(processPath, localAppData);

        Assert.AreEqual(Path.GetFullPath(legacyRoot), result);
    }

    [TestMethod]
    public void GetLegacyRootIfCurrentProcessIsPerUser_SiblingPrefixDoesNotMatch()
    {
        var localAppData = Path.Combine(testRoot, "LocalAppData");
        var processPath = Path.Combine(localAppData, "Shisui-Evil", "current", "Shisui.UI.exe");

        var result = WindowsPerMachineMigration.GetLegacyRootIfCurrentProcessIsPerUser(processPath, localAppData);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void IsPerMachineProcessPath_MsiMarkerExistsAboveCurrentDirectory_ReturnsTrue()
    {
        var programFiles = Path.Combine(testRoot, "Program Files");
        var installRoot = Path.Combine(programFiles, "ゆろち", "Shisui");
        Directory.CreateDirectory(Path.Combine(installRoot, "current"));
        File.WriteAllText(Path.Combine(installRoot, ".msi-installed"), string.Empty);
        var processPath = Path.Combine(installRoot, "current", "Shisui.UI.exe");

        Assert.IsTrue(WindowsPerMachineMigration.IsPerMachineProcessPath(processPath, programFiles));
    }

    [TestMethod]
    public void TryCleanupLegacyArtifacts_ExactRoot_DeletesFilesPackagesAndOldShortcuts()
    {
        var legacyRoot = Path.Combine(testRoot, "LocalAppData", "Shisui");
        var packageDirectory = Path.Combine(legacyRoot, "packages");
        var currentDirectory = Path.Combine(legacyRoot, "current");
        Directory.CreateDirectory(packageDirectory);
        Directory.CreateDirectory(currentDirectory);
        File.WriteAllText(Path.Combine(packageDirectory, "old.nupkg"), "old");
        File.WriteAllText(Path.Combine(currentDirectory, "Shisui.UI.exe"), "old");

        var programs = Path.Combine(testRoot, "Programs");
        var legacyPrograms = Path.Combine(programs, "ゆろち");
        var desktop = Path.Combine(testRoot, "Desktop");
        Directory.CreateDirectory(legacyPrograms);
        Directory.CreateDirectory(desktop);
        File.WriteAllText(Path.Combine(programs, "Shisui.lnk"), "old");
        File.WriteAllText(Path.Combine(legacyPrograms, "Shisui.lnk"), "old");
        File.WriteAllText(Path.Combine(desktop, "Shisui.lnk"), "old");

        var cleaned = WindowsPerMachineMigration.TryCleanupLegacyArtifacts(
            legacyRoot, legacyRoot, programs, desktop);

        Assert.IsTrue(cleaned);
        Assert.IsFalse(Directory.Exists(legacyRoot));
        Assert.IsFalse(File.Exists(Path.Combine(programs, "Shisui.lnk")));
        Assert.IsFalse(Directory.Exists(legacyPrograms));
        Assert.IsFalse(File.Exists(Path.Combine(desktop, "Shisui.lnk")));
    }

    [TestMethod]
    public void TryCleanupLegacyArtifacts_UnexpectedRoot_RefusesDeletion()
    {
        var expectedRoot = Path.Combine(testRoot, "LocalAppData", "Shisui");
        var unrelatedRoot = Path.Combine(testRoot, "Unrelated");
        Directory.CreateDirectory(unrelatedRoot);
        File.WriteAllText(Path.Combine(unrelatedRoot, "keep.txt"), "keep");

        var cleaned = WindowsPerMachineMigration.TryCleanupLegacyArtifacts(
            unrelatedRoot, expectedRoot, Path.Combine(testRoot, "Programs"), Path.Combine(testRoot, "Desktop"));

        Assert.IsFalse(cleaned);
        Assert.IsTrue(File.Exists(Path.Combine(unrelatedRoot, "keep.txt")));
    }
}
