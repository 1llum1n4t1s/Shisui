using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public sealed class LegacyLogMigrationTests
{
    [TestMethod]
    public void InstallerCleanupAndLogMigration_PreserveLogsFromBothOldLocations()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "shisui-log-migration-" + Guid.NewGuid().ToString("N"));
        var localRoot = Path.Combine(root, "local", "Shisui");
        var destination = Path.Combine(localRoot, "logs");
        var roaming = Path.Combine(root, "roaming", "Shisui", "logs");
        var kagayoi = Path.Combine(root, "local", "Kagayoi", "Shisui", "logs");
        var mutexName = "Shisui.Test." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(localRoot, "current"));
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(roaming);
        Directory.CreateDirectory(kagayoi);
        File.WriteAllText(Path.Combine(localRoot, "current", "Shisui.UI.exe"), "old installer");
        File.WriteAllText(Path.Combine(destination, "existing.log"), "existing");
        File.WriteAllText(Path.Combine(roaming, "roaming.log"), "roaming");
        File.WriteAllText(Path.Combine(kagayoi, "kagayoi.log"), "kagayoi");
        File.WriteAllText(Path.Combine(root, "local", "Kagayoi", "keep.txt"), "keep");

        Assert.IsTrue(WindowsPerMachineMigration.TryCleanupLegacyArtifacts(localRoot, localRoot,
            Path.Combine(root, "programs"), Path.Combine(root, "desktop"), preserveLogs: true));
        Assert.IsFalse(Directory.Exists(Path.Combine(localRoot, "current")));
        Assert.AreEqual("existing", File.ReadAllText(Path.Combine(destination, "existing.log")));
        LegacyLogMigration.TryMigrate(roaming, destination, mutexName);
        LegacyLogMigration.TryMigrate(kagayoi, destination, mutexName, removeEmptyParent: true);
        Assert.IsTrue(WindowsPerMachineMigration.TryCleanupLegacyArtifacts(localRoot, localRoot,
            Path.Combine(root, "programs"), Path.Combine(root, "desktop"), preserveLogs: true));

        Assert.AreEqual("roaming", File.ReadAllText(Path.Combine(destination, "roaming.log")));
        Assert.AreEqual("kagayoi", File.ReadAllText(Path.Combine(destination, "kagayoi.log")));
        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(root, "local", "Kagayoi", "keep.txt")));
        Assert.IsFalse(Directory.Exists(roaming));
        Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(roaming)));
        Assert.IsFalse(Directory.Exists(kagayoi));
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(kagayoi)));
    }

    [TestMethod]
    public void Migrate_EmptyParentCleanupPreservesOtherFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "shisui-log-migration-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "Kagayoi", "Shisui", "logs");
        var destination = Path.Combine(root, "destination");
        var parent = Path.GetDirectoryName(source)!;
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(parent, "keep.txt"), "keep");
        File.WriteAllText(Path.Combine(source, "old.log"), "old");

        LegacyLogMigration.TryMigrate(source, destination, "Shisui.Test." + Guid.NewGuid().ToString("N"), removeEmptyParent: true);

        Assert.IsFalse(Directory.Exists(source));
        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(parent, "keep.txt")));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(destination, "old.log")));
    }

    [TestMethod]
    public void Migrate_PreservesCollisionsAndLockedFilesThenRetries()
    {
        var root = Path.Combine(Path.GetTempPath(), "shisui-log-migration-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "old", "logs");
        var destination = Path.Combine(root, "new", "logs");
        var mutexName = "Shisui.Test." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(source, "same.log"), "old");
        File.WriteAllText(Path.Combine(destination, "same.log"), "new");
        File.WriteAllText(Path.Combine(source, "locked.log"), "locked");
        File.WriteAllText(Path.Combine(source, "settings.txt"), "keep");

        using (new FileStream(Path.Combine(source, "locked.log"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            LegacyLogMigration.TryMigrate(source, destination, mutexName);
            Assert.IsTrue(File.Exists(Path.Combine(source, "locked.log")));
        }

        Assert.AreEqual("new", File.ReadAllText(Path.Combine(destination, "same.log")));
        Assert.AreEqual("old", File.ReadAllText(Directory.GetFiles(destination, "same-*.log").Single()));
        Assert.IsTrue(File.Exists(Path.Combine(source, "settings.txt")));
        LegacyLogMigration.TryMigrate(source, destination, mutexName);
        Assert.AreEqual("locked", File.ReadAllText(Path.Combine(destination, "locked.log")));
        Assert.IsTrue(Directory.Exists(source));
    }

    [TestMethod]
    public void Migrate_ConcurrentAttemptsKeepEveryLogAndRemoveEmptySource()
    {
        var root = Path.Combine(Path.GetTempPath(), "shisui-log-migration-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "old", "logs");
        var destination = Path.Combine(root, "new", "logs");
        var mutexName = "Shisui.Test." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(source);
        for (var i = 0; i < 20; i++) File.WriteAllText(Path.Combine(source, $"{i}.log"), $"content-{i}");

        Parallel.For(0, 4, _ => LegacyLogMigration.TryMigrate(source, destination, mutexName));

        Assert.IsFalse(Directory.Exists(source));
        for (var i = 0; i < 20; i++)
            Assert.AreEqual($"content-{i}", File.ReadAllText(Path.Combine(destination, $"{i}.log")));
    }

    [TestMethod]
    public void Migrate_DoesNotFollowLinkedSourceOrDestination()
    {
        var root = Path.Combine(Path.GetTempPath(), "shisui-log-migration-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "old", "logs");
        var destination = Path.Combine(root, "new", "logs");
        var external = Path.Combine(root, "external");
        var mutexName = "Shisui.Test." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(external, "external.log"), "external");
        var linkedDirectory = Path.Combine(root, "linked");
        CreateDirectoryLink(linkedDirectory, external);
        LegacyLogMigration.TryMigrate(linkedDirectory, destination, mutexName);
        Assert.AreEqual("external", File.ReadAllText(Path.Combine(external, "external.log")));
        Assert.IsFalse(Directory.Exists(destination));

        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "source.log"), "source");
        LegacyLogMigration.TryMigrate(source, linkedDirectory, mutexName);
        Assert.AreEqual("source", File.ReadAllText(Path.Combine(source, "source.log")));
        Assert.IsFalse(File.Exists(Path.Combine(external, "source.log")));
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode);
    }
}
