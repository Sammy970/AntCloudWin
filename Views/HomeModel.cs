using AntCloud.Core;
using AntCloud.Core.Api;
using AntCloud.Core.Auth;
using AntCloud.Core.Session;
using AntCloud.Core.Stream;

namespace AntCloud.Views;

/// Everything the signed-in window works with, shared by its panes. Kept for the window's lifetime so a
/// network check's results survive switching panes.
sealed class HomeModel
{
    public AuthStore Auth { get; }
    public SessionService Session { get; }
    public StreamSettings Settings { get; } = StreamSettings.Load();
    public NetworkCheck Network { get; } = new();
    public UpstreamStatus Upstream => UpstreamStatus.Shared;

    public HomeModel(AuthStore auth)
    {
        Auth = auth;
        Session = new SessionService(auth.Api, new MoonlightDriver());
        Session.OnSessionExpired = auth.Logout;
    }

    public static bool MoonlightInstalled => MoonlightRunner.IsInstalled;

    /// Opens a web page in the default browser.
    public static void Open(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) { Log.Info($"[ui] couldn't open {url}: {e.Message}"); }
    }
}
