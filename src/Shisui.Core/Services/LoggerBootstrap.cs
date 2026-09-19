using Microsoft.Extensions.Logging;
using SuperLightLogger;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Shisui.Core.Models;

namespace Shisui.Core.Services;

/// <summary>
/// SuperLightLogger の初期化・終了処理をまとめたエントリポイント。
/// ログ出力先は AppPaths.LogsDirectory (OS ごとの正しいアプリデータフォルダ配下)。
/// </summary>
public static class LoggerBootstrap
{
    private static ILog? _log;
    private static readonly AsyncLocal<string?> CurrentOperation = new();
    internal static string OperationId => CurrentOperation.Value ?? "none";

    public static ILog Log => _log ??= LogManager.GetLogger(typeof(LoggerBootstrap));

    public static void Initialize()
    {
        Directory.CreateDirectory(AppPaths.LogsDirectory);

        LogManager.Configure(builder =>
        {
            builder.AddSuperLightFile(Path.Combine(AppPaths.LogsDirectory, "Shisui_${shortdate}.log"));
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var version = typeof(LoggerBootstrap).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        var administrator = "not-applicable";
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            administrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator).ToString();
        }
        Log.Info($"SessionStart id={Guid.NewGuid():N} at={DateTimeOffset.Now:O} pid={Environment.ProcessId} " +
                 $"version={version} os={RuntimeInformation.OSDescription} osVersion={Environment.OSVersion.Version} " +
                 $"architecture={RuntimeInformation.ProcessArchitecture} runtime={RuntimeInformation.FrameworkDescription} " +
                 $"culture={CultureInfo.CurrentCulture.Name} uiCulture={CultureInfo.CurrentUICulture.Name} administrator={administrator}");
    }

    /// <summary>遅延した画面通知と実行時ログを区別し、適用後確認などの合成結果も出力する。</summary>
    public static void LogCommandResult(CommandExecutionResult result) =>
        CommandExecutionTrace.Write(result.DiagnosticId is { } id
            ? $"CommandReported id={id} (UI notification, not execution time)"
            : $"CommandReported operation={OperationId} (synthetic result)\n" + CommandExecutionTrace.FormatResult(result), !result.Success);

    /// <summary>複合操作のコマンドと確認結果を、非同期処理をまたぐIDで関連付ける。</summary>
    public static IDisposable BeginOperation(string name, string adapter)
    {
        var previous = CurrentOperation.Value;
        var id = Guid.NewGuid().ToString("N");
        CurrentOperation.Value = id;
        Log.Info($"OperationStart id={id} at={DateTimeOffset.Now:O} name={name} adapter={adapter}");
        return new OperationScope(id, previous);
    }

    private sealed class OperationScope(string id, string? previous) : IDisposable
    {
        public void Dispose()
        {
            Log.Info($"OperationEnd id={id} at={DateTimeOffset.Now:O} (scope closed; see command results)");
            CurrentOperation.Value = previous;
        }
    }

    public static void Shutdown() => LogManager.Shutdown();
}
