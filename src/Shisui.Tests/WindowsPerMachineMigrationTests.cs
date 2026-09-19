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
    public void IsMsiProcessPath_CurrentProcessIsInsideRegisteredRoot_ReturnsTrue()
    {
        var installRoot = Path.Combine(testRoot, "CustomLocation", "Shisui");
        Directory.CreateDirectory(Path.Combine(installRoot, "current"));
        File.WriteAllText(Path.Combine(installRoot, ".msi-installed"), string.Empty);
        var processPath = Path.Combine(installRoot, "current", "Shisui.UI.exe");
        var registeredExecutable = Path.Combine(installRoot, "Shisui.exe");

        Assert.IsTrue(WindowsPerMachineMigration.IsMsiProcessPath(processPath, registeredExecutable));
    }

    [TestMethod]
    public void IsMsiProcessPath_DifferentInstallRoot_ReturnsFalse()
    {
        var installRoot = Path.Combine(testRoot, "Installed", "Shisui");
        Directory.CreateDirectory(installRoot);
        File.WriteAllText(Path.Combine(installRoot, ".msi-installed"), string.Empty);
        var processPath = Path.Combine(testRoot, "OtherLocation", "current", "Shisui.UI.exe");
        var registeredExecutable = Path.Combine(installRoot, "Shisui.exe");

        Assert.IsFalse(WindowsPerMachineMigration.IsMsiProcessPath(processPath, registeredExecutable));
    }

    [TestMethod]
    public void IsMsiProcessPath_MsiMarkerIsMissing_ReturnsFalse()
    {
        var installRoot = Path.Combine(testRoot, "Installed", "Shisui");
        var currentDirectory = Path.Combine(installRoot, "current");
        Directory.CreateDirectory(currentDirectory);
        var processPath = Path.Combine(currentDirectory, "Shisui.UI.exe");
        var registeredExecutable = Path.Combine(installRoot, "Shisui.exe");

        Assert.IsFalse(WindowsPerMachineMigration.IsMsiProcessPath(processPath, registeredExecutable));
    }

    [TestMethod]
    public void GetKnownMisplacedPerMachineRoot_DriveRootInstall_ReturnsRoot()
    {
        var programFiles = Path.Combine(testRoot, "Program Files");
        var misplacedRoot = Path.Combine(Path.GetPathRoot(programFiles)!, "Shisui");

        var result = WindowsPerMachineMigration.GetKnownMisplacedPerMachineRoot(
            Path.Combine(misplacedRoot, "Shisui.exe"),
            programFiles);

        Assert.AreEqual(Path.GetFullPath(misplacedRoot), result);
    }

    [TestMethod]
    public void GetKnownMisplacedPerMachineRoot_AuthorFolderInstall_ReturnsRoot()
    {
        var programFiles = Path.Combine(testRoot, "Program Files");
        var misplacedRoot = Path.Combine(programFiles, "ゆろち", "Shisui");

        var result = WindowsPerMachineMigration.GetKnownMisplacedPerMachineRoot(
            Path.Combine(misplacedRoot, "Shisui.exe"),
            programFiles);

        Assert.AreEqual(Path.GetFullPath(misplacedRoot), result);
    }

    [TestMethod]
    public void GetKnownMisplacedPerMachineRoot_CorrectOrCustomInstall_ReturnsNull()
    {
        var programFiles = Path.Combine(testRoot, "Program Files");

        Assert.IsNull(WindowsPerMachineMigration.GetKnownMisplacedPerMachineRoot(
            Path.Combine(programFiles, "Shisui", "Shisui.exe"),
            programFiles));
        Assert.IsNull(WindowsPerMachineMigration.GetKnownMisplacedPerMachineRoot(
            Path.Combine(testRoot, "Custom", "Shisui", "Shisui.exe"),
            programFiles));
    }

    [TestMethod]
    public void GetApprovedLegacyCleanupRoot_ProtectedMarkerOnlyAllowsKnownRoots()
    {
        var programFiles = Path.Combine(testRoot, "Program Files");
        var expectedPerUserRoot = Path.Combine(testRoot, "LocalAppData", "Shisui");
        var driveRootInstall = Path.Combine(Path.GetPathRoot(programFiles)!, "Shisui");
        var authorInstall = Path.Combine(programFiles, "ゆろち", "Shisui");

        Assert.AreEqual(
            Path.GetFullPath(expectedPerUserRoot),
            WindowsPerMachineMigration.GetApprovedLegacyCleanupRoot(
                expectedPerUserRoot, expectedPerUserRoot, programFiles, true));
        Assert.AreEqual(
            Path.GetFullPath(driveRootInstall),
            WindowsPerMachineMigration.GetApprovedLegacyCleanupRoot(
                driveRootInstall, expectedPerUserRoot, programFiles, true));
        Assert.AreEqual(
            Path.GetFullPath(authorInstall),
            WindowsPerMachineMigration.GetApprovedLegacyCleanupRoot(
                authorInstall, expectedPerUserRoot, programFiles, true));
        Assert.IsNull(WindowsPerMachineMigration.GetApprovedLegacyCleanupRoot(
            Path.Combine(testRoot, "Unrelated", "Shisui"),
            expectedPerUserRoot,
            programFiles,
            true));
        Assert.IsNull(WindowsPerMachineMigration.GetApprovedLegacyCleanupRoot(
            driveRootInstall,
            expectedPerUserRoot,
            programFiles,
            false));
    }

    [TestMethod]
    public void HasLegacyInstallationArtifacts_UpdateExeOrPackagesOnly_ReturnsTrue()
    {
        var legacyRoot = Path.Combine(testRoot, "LocalAppData", "Shisui");
        Directory.CreateDirectory(legacyRoot);
        File.WriteAllText(Path.Combine(legacyRoot, "Update.exe"), "old");

        Assert.IsTrue(WindowsPerMachineMigration.HasLegacyInstallationArtifacts(legacyRoot));

        File.Delete(Path.Combine(legacyRoot, "Update.exe"));
        Directory.CreateDirectory(Path.Combine(legacyRoot, "packages"));

        Assert.IsTrue(WindowsPerMachineMigration.HasLegacyInstallationArtifacts(legacyRoot));
    }

    [TestMethod]
    public void HasLegacyInstallationArtifacts_UnrelatedEmptyRoot_ReturnsFalse()
    {
        var legacyRoot = Path.Combine(testRoot, "LocalAppData", "Shisui");
        Directory.CreateDirectory(legacyRoot);

        Assert.IsFalse(WindowsPerMachineMigration.HasLegacyInstallationArtifacts(legacyRoot));
    }

    [TestMethod]
    public void CreateMsiInstallStartInfo_ForcesProgramFilesShisui()
    {
        var msiPath = Path.Combine(testRoot, "Shisui-win.msi");
        var systemDirectory = Path.Combine(testRoot, "Windows", "System32");
        var programFilesDirectory = Path.Combine(testRoot, "Program Files");

        var startInfo = WindowsPerMachineMigration.CreateMsiInstallStartInfo(
            msiPath,
            systemDirectory,
            programFilesDirectory);

        Assert.AreEqual(Path.Combine(systemDirectory, "msiexec.exe"), startInfo.FileName);
        Assert.IsTrue(startInfo.UseShellExecute);
        Assert.AreEqual("runas", startInfo.Verb);
        Assert.AreEqual(
            $"/i \"{msiPath}\" VELOPACK_INSTALLDIR=\"{Path.Combine(programFilesDirectory, "Shisui")}\" /passive /norestart",
            startInfo.Arguments);
        Assert.AreEqual(0, startInfo.ArgumentList.Count);
    }

    [TestMethod]
    public void CreateMsiInstallStartInfo_RelativeMsiPath_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => WindowsPerMachineMigration.CreateMsiInstallStartInfo(
            "Shisui-win.msi",
            Path.Combine(testRoot, "Windows", "System32"),
            Path.Combine(testRoot, "Program Files")));
    }

    [TestMethod]
    public void CreateMsiInstallStartInfo_ProgramFilesIsDriveRoot_Throws()
    {
        var driveRoot = Path.GetPathRoot(testRoot)!;

        Assert.ThrowsExactly<InvalidOperationException>(() => WindowsPerMachineMigration.CreateMsiInstallStartInfo(
            Path.Combine(testRoot, "Shisui-win.msi"),
            Path.Combine(testRoot, "Windows", "System32"),
            driveRoot));
    }

    [TestMethod]
    public void CreateMsiInstallStartInfo_ReinstallForcesAllFeaturesAndRecachesPackage()
    {
        var msiPath = Path.Combine(testRoot, "Shisui-win.msi");
        var systemDirectory = Path.Combine(testRoot, "Windows", "System32");
        var programFilesDirectory = Path.Combine(testRoot, "Program Files");

        var startInfo = WindowsPerMachineMigration.CreateMsiInstallStartInfo(
            msiPath,
            systemDirectory,
            programFilesDirectory,
            reinstallExistingProduct: true);

        Assert.AreEqual(
            $"/i \"{msiPath}\" VELOPACK_INSTALLDIR=\"{Path.Combine(programFilesDirectory, "Shisui")}\" REINSTALL=ALL REINSTALLMODE=vamus /passive /norestart",
            startInfo.Arguments);
        Assert.AreEqual(0, startInfo.ArgumentList.Count);
    }

    [TestMethod]
    public void OpenLockedMsiDownloadTarget_HeldHandle_BlocksReplacementUntilDisposed()
    {
        var stagingDirectory = Path.Combine(testRoot, "staging");
        var migrationDirectory = Path.Combine(stagingDirectory, "per-machine-migration");
        var downloadDirectory = Path.Combine(migrationDirectory, "download");
        var movedDirectory = Path.Combine(testRoot, "moved");
        var movedStagingDirectory = Path.Combine(testRoot, "moved-staging");
        Directory.CreateDirectory(downloadDirectory);
        var msiPath = Path.Combine(downloadDirectory, "Shisui-win.msi");

        using (var lockedMsi = WindowsPerMachineMigration.OpenLockedMsiDownloadTarget(msiPath))
        {
            lockedMsi.WriteByte(0x42);
            lockedMsi.Flush(flushToDisk: true);

            Assert.ThrowsExactly<IOException>(() => File.OpenWrite(msiPath));
            Assert.ThrowsExactly<IOException>(() => File.Delete(msiPath));
            Assert.ThrowsExactly<IOException>(() => File.Move(msiPath, msiPath + ".replaced"));
            Assert.ThrowsExactly<IOException>(() => Directory.Move(downloadDirectory, movedDirectory));
            Assert.ThrowsExactly<IOException>(() => Directory.Move(stagingDirectory, movedStagingDirectory));
        }

        File.Delete(msiPath);
        Directory.Move(stagingDirectory, movedStagingDirectory);
        Assert.IsTrue(Directory.Exists(Path.Combine(movedStagingDirectory, "per-machine-migration", "download")));
    }

    [TestMethod]
    public void TryDeleteTreeWithoutFollowingReparsePoints_LockedFile_RemovesUnlockedSiblingAndRetries()
    {
        var legacyRoot = Path.Combine(testRoot, "LocalAppData", "Shisui");
        Directory.CreateDirectory(legacyRoot);
        var lockedPath = Path.Combine(legacyRoot, "000-locked.bin");
        var removablePath = Path.Combine(legacyRoot, "999-removable.bin");
        File.WriteAllText(lockedPath, "locked");
        File.WriteAllText(removablePath, "removable");

        using (var lockedFile = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            WindowsPerMachineMigration.TryDeleteTreeWithoutFollowingReparsePoints(legacyRoot);

            Assert.IsTrue(File.Exists(lockedPath));
            Assert.IsFalse(File.Exists(removablePath));
            Assert.IsTrue(Directory.Exists(legacyRoot));
        }

        WindowsPerMachineMigration.TryDeleteTreeWithoutFollowingReparsePoints(legacyRoot);

        Assert.IsFalse(Directory.Exists(legacyRoot));
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

        string? refreshedDirectory = null;
        var cleaned = WindowsPerMachineMigration.TryCleanupLegacyArtifacts(
            legacyRoot,
            legacyRoot,
            programs,
            desktop,
            directory => refreshedDirectory = directory);

        Assert.IsTrue(cleaned);
        Assert.IsFalse(Directory.Exists(legacyRoot));
        Assert.IsFalse(File.Exists(Path.Combine(programs, "Shisui.lnk")));
        Assert.IsFalse(Directory.Exists(legacyPrograms));
        Assert.IsFalse(File.Exists(Path.Combine(desktop, "Shisui.lnk")));
        Assert.AreEqual(programs, refreshedDirectory);
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
