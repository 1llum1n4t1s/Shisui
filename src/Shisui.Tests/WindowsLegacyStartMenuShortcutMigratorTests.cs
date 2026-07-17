using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsLegacyStartMenuShortcutMigratorTests
{
    private string _programsDirectory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _programsDirectory = Path.Combine(Path.GetTempPath(), "Shisui.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_programsDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_programsDirectory))
        {
            Directory.Delete(_programsDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void TryMigrate_LegacyShortcutExists_MovesItToStartMenuRootAndRemovesEmptyLegacyFolder()
    {
        var legacyDirectory = Directory.CreateDirectory(Path.Combine(_programsDirectory, "ゆろち"));
        var legacyShortcut = Path.Combine(legacyDirectory.FullName, "Shisui.lnk");
        File.WriteAllText(legacyShortcut, "legacy shortcut");

        var migrated = WindowsLegacyStartMenuShortcutMigrator.TryMigrate(_programsDirectory);

        Assert.IsTrue(migrated);
        Assert.IsFalse(File.Exists(legacyShortcut));
        Assert.IsFalse(Directory.Exists(legacyDirectory.FullName));
        Assert.AreEqual("legacy shortcut", File.ReadAllText(Path.Combine(_programsDirectory, "Shisui.lnk")));
    }

    [TestMethod]
    public void TryMigrate_RootShortcutAlreadyExists_PreservesBothShortcuts()
    {
        var legacyDirectory = Directory.CreateDirectory(Path.Combine(_programsDirectory, "ゆろち"));
        var legacyShortcut = Path.Combine(legacyDirectory.FullName, "Shisui.lnk");
        var rootShortcut = Path.Combine(_programsDirectory, "Shisui.lnk");
        File.WriteAllText(legacyShortcut, "legacy shortcut");
        File.WriteAllText(rootShortcut, "existing shortcut");

        var migrated = WindowsLegacyStartMenuShortcutMigrator.TryMigrate(_programsDirectory);

        Assert.IsFalse(migrated);
        Assert.AreEqual("legacy shortcut", File.ReadAllText(legacyShortcut));
        Assert.AreEqual("existing shortcut", File.ReadAllText(rootShortcut));
    }

    [TestMethod]
    public void TryMigrate_LegacyFolderContainsAnotherFile_KeepsTheFolder()
    {
        var legacyDirectory = Directory.CreateDirectory(Path.Combine(_programsDirectory, "ゆろち"));
        File.WriteAllText(Path.Combine(legacyDirectory.FullName, "Shisui.lnk"), "legacy shortcut");
        var otherFile = Path.Combine(legacyDirectory.FullName, "別のアプリ.lnk");
        File.WriteAllText(otherFile, "other shortcut");

        var migrated = WindowsLegacyStartMenuShortcutMigrator.TryMigrate(_programsDirectory);

        Assert.IsTrue(migrated);
        Assert.IsTrue(Directory.Exists(legacyDirectory.FullName));
        Assert.AreEqual("other shortcut", File.ReadAllText(otherFile));
        Assert.IsTrue(File.Exists(Path.Combine(_programsDirectory, "Shisui.lnk")));
    }
}
