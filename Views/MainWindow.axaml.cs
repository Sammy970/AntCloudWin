using System.Text.Json.Nodes;
using AntCloud.Core;
using AntCloud.Core.Api;
using AntCloud.Core.Auth;
using AntCloud.Core.Session;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace AntCloud.Views;

/// The one window: a spinner while signing in, then the sign-in screen or the signed-in home.
partial class MainWindow : Window
{
    private readonly AuthStore auth;
    private HomeModel? home;
    private readonly Queue<(string Title, string Message)> notices = new();
    private TaskCompletionSource<bool>? confirm;
    private bool confirmedClose;

    public MainWindow() : this(new AuthStore()) { }

    public MainWindow(AuthStore auth)
    {
        this.auth = auth;
        InitializeComponent();
        NoticeCard.BoxShadow = Controls.Motion.CardShadow("0 16 48 0 #48000000");
        // macOS (development) puts its window buttons on the left.
        if (OperatingSystem.IsMacOS()) TitleBar.Margin = new Thickness(84, 0, 16, 0);

        NoticeOk.Click += (_, _) => CloseNotice(true);
        NoticeSecondary.Click += (_, _) => CloseNotice(false);
        KeyDown += (_, e) =>
        {
            if (!NoticeLayer.IsVisible) return;
            if (e.Key == Key.Escape) { CloseNotice(false); e.Handled = true; }
            if (e.Key == Key.Enter) { CloseNotice(true); e.Handled = true; }
        };

        auth.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AuthStore.State)) ShowState(); };
        ShowState();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // Mica on Windows 11 shows the desktop's tint through the window; elsewhere the solid background stays.
        if (ActualTransparencyLevel == WindowTransparencyLevel.Mica) Background = Brushes.Transparent;
        if (!StartPreview()) _ = auth.Restore();
        if (Environment.GetEnvironmentVariable("ANTCLOUD_SNAPSHOT") is { } path) _ = Snapshot(path);
    }

    private void ShowState()
    {
        switch (auth.State)
        {
            case AuthStore.AuthState.Restoring:
                Root.Content = new StackPanel
                {
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Spacing = 14,
                    Children =
                    {
                        new ProgressBar { IsIndeterminate = true, Width = 160, MinWidth = 0 },
                        new TextBlock { Text = "Signing in…", Classes = { "secondary" } },
                    },
                };
                break;
            case AuthStore.AuthState.LoggedOut:
                home = null;
                Root.Content = new LoginView(auth);
                break;
            case AuthStore.AuthState.LoggedIn:
                home = new HomeModel(auth);
                Root.Content = new HomeView(home);
                PreviewHome();
                break;
        }
    }

    // Notices

    /// Shows a notice in the window that holds `from`. Notices that arrive together queue up.
    public static void ShowNotice(Visual from, string title, string message)
    {
        if (TopLevel.GetTopLevel(from) is MainWindow window) window.Enqueue(title, message);
    }

    private void Enqueue(string title, string message)
    {
        notices.Enqueue((title, message));
        if (!NoticeLayer.IsVisible) ShowNext();
    }

    private void ShowNext()
    {
        if (!notices.TryDequeue(out var notice)) { NoticeLayer.IsVisible = false; return; }
        NoticeTitle.Text = notice.Title;
        NoticeMessage.Text = notice.Message;
        NoticeOk.Content = "OK";
        NoticeSecondary.IsVisible = false;
        NoticeLayer.IsVisible = true;
        NoticeOk.Focus();
    }

    private Task<bool> Confirm(string title, string message, string action)
    {
        NoticeTitle.Text = title;
        NoticeMessage.Text = message;
        NoticeOk.Content = action;
        NoticeSecondary.Content = "Cancel";
        NoticeSecondary.IsVisible = true;
        NoticeLayer.IsVisible = true;
        NoticeOk.Focus();
        confirm = new TaskCompletionSource<bool>();
        return confirm.Task;
    }

    private void CloseNotice(bool ok)
    {
        if (confirm is { } pending)
        {
            confirm = null;
            NoticeLayer.IsVisible = false;
            pending.SetResult(ok);
            if (notices.Count > 0) ShowNext();
            return;
        }
        ShowNext();
    }

    // Closing

    /// Closing the window while the cloud PC runs shuts it down, after asking: it's billed time.
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (confirmedClose || home is not { Session.IsActive: true } model) return;
        e.Cancel = true;
        var ok = await Confirm("Shut down your cloud PC?",
                               model.Session.Phase == Phase.Streaming
                                   ? "You're streaming. Quitting AntCloud ends the stream and shuts down your cloud PC."
                                   : "Quitting AntCloud shuts down your cloud PC.",
                               "Shut Down & Quit");
        if (!ok) return;
        model.Session.ShutDown();
        confirmedClose = true;
        // Gives the socket a moment to tell AntCloud the session is over.
        await Task.Delay(400);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        StreamOverlayWindow.Shared.End();
    }

    // Screenshot mode

    /// `ANTCLOUD_PREVIEW=login` or `home:<phase>[:stream|:network|:about|:overlay]` renders a state with a
    /// fake profile, without reading the stored token or calling the API.
    private bool StartPreview()
    {
        if (Environment.GetEnvironmentVariable("ANTCLOUD_PREVIEW") is not { } value) return false;
        var renew = DateTimeOffset.Now.AddDays(12).ToString("o");
        auth.PreviewSignIn(value == "login" ? null : new UserProfile(new JsonObject
        {
            ["firstName"] = "Samyak", ["email"] = "player@example.com", ["currentPlan"] = "premiumNyt",
            ["renewDate"] = renew, ["totalTimeMonth"] = 3600, ["timeUsedMonth"] = 1290,
        }));
        return true;
    }

    private void PreviewHome()
    {
        if (Environment.GetEnvironmentVariable("ANTCLOUD_PREVIEW") is not { } value || !value.StartsWith("home:") || home == null) return;
        var parts = value[5..].Split(':');
        var phases = new Dictionary<string, Phase>
        {
            ["idle"] = Phase.Idle, ["connecting"] = Phase.Connecting, ["queued"] = Phase.Queued, ["booting"] = Phase.Booting,
            ["ready"] = Phase.Ready, ["pairing"] = Phase.Pairing, ["streaming"] = Phase.Streaming,
        };
        if (phases.TryGetValue(parts[0], out var phase)) home.Session.PreviewPhase(phase, phase == Phase.Queued ? 3 : null);
        if (Root.Content is HomeView view)
            foreach (var pane in new[] { "stream", "network", "controllers", "about" })
                if (parts.Contains(pane)) view.Select(pane);
        if (parts.Contains("sample") && Root.Content is HomeView sampleView) sampleView.ShowControllerSample();
        if (parts.Contains("overlay")) StreamOverlayWindow.Shared.Begin("socket.antcloud.co", auth.Profile);
        if (parts.Contains("notice")) Enqueue("Servers at Capacity", "All servers are currently busy. Please wait 5–10 minutes and try again.");
    }

    /// `ANTCLOUD_SNAPSHOT=<file.png>` saves the window once it has settled, then quits.
    private async Task Snapshot(string path)
    {
        await Task.Delay(TimeSpan.FromSeconds(double.TryParse(Environment.GetEnvironmentVariable("ANTCLOUD_SNAPSHOT_DELAY"), out var d) ? d : 2.5));
        var scale = RenderScaling;
        var target = StreamOverlayWindow.Shared.IsVisible && Environment.GetEnvironmentVariable("ANTCLOUD_PREVIEW")?.EndsWith(":overlay") == true
            ? (Window)StreamOverlayWindow.Shared : this;
        var size = new PixelSize((int)(target.Bounds.Width * scale), (int)(target.Bounds.Height * scale));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        bitmap.Render(target);
        bitmap.Save(path);
        Log.Info($"[snapshot] saved {path}");
        Dispatcher.UIThread.Post(() => (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown());
    }
}
