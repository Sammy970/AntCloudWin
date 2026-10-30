using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AntCloud.Core.Api;

/// Notices when AntCloud changes its service under this app.
///
/// AntCloud has renamed API routes alongside a new Windows client (0.015 → 0.016 on 2026-10-30 renamed
/// every route). So the app checks the published version on launch, and the API client reports any route
/// the server says no longer exists. Either one shows the upstream banner.
sealed class UpstreamStatus : ObservableObject
{
    public static UpstreamStatus Shared { get; } = new();

    private const string DismissedKey = "dismissedUpstreamVersion";

    private UpstreamStatus()
    {
        // Screenshot mode: `ANTCLOUD_UPSTREAM=version:0.017` or `route:POST phoneLogin` shows the banner.
        if (Environment.GetEnvironmentVariable("ANTCLOUD_UPSTREAM") is { } value)
        {
            if (value.StartsWith("version:")) newerClientVersion = double.Parse(value[8..], System.Globalization.CultureInfo.InvariantCulture);
            if (value.StartsWith("route:")) missingRoute = value[6..];
        }
    }

    private double? newerClientVersion;
    /// AntCloud's current Windows client version, when it's newer than the one we match.
    public double? NewerClientVersion
    {
        get => newerClientVersion;
        private set { if (SetProperty(ref newerClientVersion, value)) Changed(); }
    }

    private string? missingRoute;
    /// A request the server answered with "route doesn't exist", e.g. "POST phoneLogin".
    public string? MissingRoute
    {
        get => missingRoute;
        private set { if (SetProperty(ref missingRoute, value)) Changed(); }
    }

    public bool HasBanner => MissingRoute != null || NewerClientVersion != null;
    public bool IsError => MissingRoute != null;
    public bool Dismissible => MissingRoute == null && NewerClientVersion != null;

    public string Title => MissingRoute != null ? "AntCloud changed their service" : "AntCloud updated their app";

    public string Message => MissingRoute is { } route
        ? $"Part of it this app uses ({route}) no longer exists, so some things won't work until this app is updated."
        : NewerClientVersion is { } version
            ? $"Their Windows app is now version {version:0.000}; this app matches {Config.MatchedClientVersion:0.000}. Past updates have changed sign-in and streaming, so if something stops working, this app needs updating."
            : "";

    public async Task Check(ApiClient api)
    {
        if (await api.WindowsClientVersion() is not { } latest) return;
        var dismissed = Preferences.Get<double>(DismissedKey);
        if (latest > Config.MatchedClientVersion + 0.0001 && Math.Abs(latest - dismissed) > 0.00001)
        {
            Log.Info($"[upstream] AntCloud's Windows client is {latest}; this app matches {Config.MatchedClientVersion}");
            Dispatcher.UIThread.Post(() => NewerClientVersion = latest);
        }
    }

    public void MarkRouteMissing(string route) => Dispatcher.UIThread.Post(() => MissingRoute = route);

    /// Hides the version notice until AntCloud releases another one. A missing route stays visible.
    public void DismissVersionNotice()
    {
        if (NewerClientVersion is { } v) Preferences.Set(DismissedKey, v);
        NewerClientVersion = null;
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(HasBanner));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(Dismissible));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Message));
    }
}
