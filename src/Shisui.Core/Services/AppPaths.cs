namespace Shisui.Core.Services;

/// <summary>
/// 設定・ログの保存先を OS ごとの正しい慣習で解決する。
/// Windows: 設定は %APPDATA%\Shisui、ログは %LOCALAPPDATA%\Shisui\logs。
/// macOS: ~/Library/Application Support/Shisui。
/// </summary>
public static class AppPaths
{
    private const string AppFolderName = "Shisui";

    public static string AppDataDirectory
    {
        get
        {
            if (OperatingSystem.IsMacOS())
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, "Library", "Application Support", AppFolderName);
            }

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, AppFolderName);
        }
    }

    public static string SettingsFilePath => Path.Combine(AppDataDirectory, "settings.json");

    public static string LogsDirectory => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName, "logs")
        : Path.Combine(AppDataDirectory, "logs");
}
