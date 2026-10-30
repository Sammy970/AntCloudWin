using AntCloud.Core;
using AntCloud.Core.Auth;
using AntCloud.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace AntCloud;

partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Screenshot mode can force a theme: `ANTCLOUD_THEME=light|dark`.
        RequestedThemeVariant = Environment.GetEnvironmentVariable("ANTCLOUD_THEME") switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Log.Info($"[app] AntCloud {Config.AppVersion} on {Environment.OSVersion}");
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = new MainWindow(new AuthStore());
        }
        base.OnFrameworkInitializationCompleted();
    }
}
