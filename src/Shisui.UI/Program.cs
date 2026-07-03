using Avalonia;
using Shisui.Core.Services;
using Velopack;

namespace Shisui.UI;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack のブートストラップを最初に走らせる
        // (--veloapp-install / --veloapp-updated 等の internal hook を捌くため、Avalonia 起動・多重起動ガードより前に必須)
        VelopackApp.Build().Run();

        using var singleInstance = new SingleInstanceGuard("Shisui");
        if (!singleInstance.TryAcquire())
        {
            return 1;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
