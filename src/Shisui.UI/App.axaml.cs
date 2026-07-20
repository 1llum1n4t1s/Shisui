using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Shisui.Core.Interfaces;
using Shisui.Core.Services;
using Shisui.UI.ViewModels;
using Shisui.UI.Views;

namespace Shisui.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LoggerBootstrap.Log.Error("UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LoggerBootstrap.Log.Error("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        LoggerBootstrap.Initialize();

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
            };
            desktop.Exit += (_, _) => LoggerBootstrap.Shutdown();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        services.AddSingleton<ISettingsService, SettingsService>();

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<ICommandExecutor, ProcessCommandExecutor>();
            services.AddSingleton<INetworkAdapterService, Core.Services.Windows.WindowsNetworkAdapterService>();
            services.AddSingleton<IDnsConfigurationService, Core.Services.Windows.WindowsDnsConfigurationService>();
            services.AddSingleton<IDohConfigurationService, Core.Services.Windows.WindowsDohConfigurationService>();
            services.AddSingleton<IDotConfigurationService, Core.Services.Windows.WindowsDotConfigurationService>();
            services.AddSingleton<IDnsCacheService, Core.Services.Windows.WindowsDnsCacheService>();
            services.AddSingleton<ITcpTuningService, Core.Services.Windows.WindowsTcpTuningService>();
            services.AddSingleton<ILoadedPingMeasurementService, Core.Services.Windows.WindowsLoadedPingMeasurementService>();
            services.AddSingleton<IAutoTuningBenchmarkService, Core.Services.Windows.WindowsAutoTuningBenchmarkService>();
            services.AddSingleton<IRscBenchmarkService, Core.Services.Windows.WindowsRscBenchmarkService>();
            services.AddSingleton<INetworkMaintenanceService, Core.Services.Windows.WindowsNetworkMaintenanceService>();
            services.AddSingleton<IGhostAdapterService, Core.Services.Windows.WindowsGhostAdapterService>();
            services.AddSingleton<INetworkDiagnosticsService, Core.Services.Windows.WindowsNetworkDiagnosticsService>();
            services.AddSingleton<TcpTuningViewModel>();
            services.AddSingleton<MaintenanceViewModel>();
        }
        else if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<ICommandExecutor, Core.Services.MacOS.MacElevatedCommandExecutor>();
            services.AddSingleton<INetworkAdapterService, Core.Services.MacOS.MacNetworkAdapterService>();
            services.AddSingleton<IDnsConfigurationService, Core.Services.MacOS.MacDnsConfigurationService>();
            services.AddSingleton<IDnsCacheService, Core.Services.MacOS.MacDnsCacheService>();
            services.AddSingleton<INetworkDiagnosticsService, Core.Services.MacOS.MacNetworkDiagnosticsService>();
            // BBR2 / TCP グローバル調整・任意実行メンテナンスコマンド・DoH は Windows (netsh) 専用機能のため
            // macOS には登録しない。MainWindowViewModel は TcpTuningViewModel / MaintenanceViewModel が
            // 未登録なら該当タブを表示せず、DnsSettingsViewModel は IDohConfigurationService? が null なら
            // DoH チェックボックスを表示しない。
        }
        else
        {
            throw new PlatformNotSupportedException("Shisui は Windows と macOS のみサポートしています。");
        }

        // 自動更新は Windows/macOS 両対応 (Velopack はクロスプラットフォーム)。
        services.AddSingleton(sp => new Services.UpdateService(sp.GetRequiredService<ISettingsService>().Current));
        services.AddSingleton<VersionViewModel>();

        services.AddSingleton<DnsSettingsViewModel>();
        services.AddSingleton<NetworkDiagnosticsViewModel>();
        services.AddSingleton<MainWindowViewModel>();
    }
}
