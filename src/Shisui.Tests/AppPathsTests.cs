using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services;

namespace Shisui.Tests;

[TestClass]
public sealed class AppPathsTests
{
    [TestMethod]
    public void LogsDirectory_UsesLocalStorageOnWindowsAndPreservesOtherPlatforms()
    {
        var expected = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shisui", "logs")
            : Path.Combine(AppPaths.AppDataDirectory, "logs");

        Assert.AreEqual(expected, AppPaths.LogsDirectory);
        Assert.AreEqual(Path.Combine(AppPaths.AppDataDirectory, "settings.json"), AppPaths.SettingsFilePath);
    }
}
