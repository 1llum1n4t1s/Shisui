using System.Text.Json;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Serialization;

namespace Shisui.Core.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly string _settingsFilePath;
    private Exception? _loadError;

    public AppSettings Current { get; }

    public SettingsService() : this(AppPaths.SettingsFilePath)
    {
    }

    public SettingsService(string settingsFilePath)
    {
        _settingsFilePath = Path.GetFullPath(settingsFilePath);
        Current = Load();
    }

    private AppSettings Load()
    {
        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize(json, ShisuiJsonContext.Default.AppSettings)
                ?? throw new JsonException("設定ファイルの内容が null です。");
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException && IsSettingsFileMissing())
        {
            // 未作成だけを、初回保存できる既定値として扱う。
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 既定値で画面を開いても、読み直すまで原本と復元情報を上書きしない。
            _loadError = ex;
            LoggerBootstrap.Log.Error($"設定ファイルを読み込めなかったため保存を停止します。path={_settingsFilePath} error={ex}");
        }

        return new AppSettings();
    }

    private bool IsSettingsFileMissing()
    {
        var path = _settingsFilePath;
        while (path is not null)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                return path != _settingsFilePath && (attributes & FileAttributes.Directory) != 0;
            }
            catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                path = Path.GetDirectoryName(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        return false;
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await _saveLock.WaitAsync(ct);
        try
        {
            if (_loadError is not null)
            {
                throw new IOException("設定を読み込めなかったため保存できません。問題を解消してアプリを再起動してください。", _loadError);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
            var json = JsonSerializer.Serialize(Current, ShisuiJsonContext.Default.AppSettings);
            var tempPath = _settingsFilePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json, ct);
            File.Move(tempPath, _settingsFilePath, overwrite: true);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
