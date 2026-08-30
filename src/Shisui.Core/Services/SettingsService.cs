using System.Text.Json;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Serialization;

namespace Shisui.Core.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public AppSettings Current { get; }

    public SettingsService()
    {
        Current = Load();
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFilePath))
            {
                var json = File.ReadAllText(AppPaths.SettingsFilePath);
                var settings = JsonSerializer.Deserialize(json, ShisuiJsonContext.Default.AppSettings);
                if (settings is not null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 破損・読み取り不可時は既定値にフォールバックする
        }

        return new AppSettings();
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await _saveLock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(AppPaths.AppDataDirectory);
            var json = JsonSerializer.Serialize(Current, ShisuiJsonContext.Default.AppSettings);
            var tempPath = AppPaths.SettingsFilePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json, ct);
            File.Move(tempPath, AppPaths.SettingsFilePath, overwrite: true);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
