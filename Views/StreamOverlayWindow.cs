using System.Runtime.InteropServices;
using AntCloud.Controls;
using AntCloud.Core;
using AntCloud.Core.Api;
using AntCloud.Core.Session;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AntCloud.Views;

/// The in-stream overlay: a small topmost window in the top-right corner, above Moonlight's borderless
/// fullscreen. It never takes focus, so the stream keeps the keyboard and mouse, and it hides whenever
/// it has nothing to show. Ctrl+Alt+A shows or hides the card; warnings appear on their own as a toast.
sealed class StreamOverlayWindow : Window
{
    public static StreamOverlayWindow Shared => shared ??= new StreamOverlayWindow();
    private static StreamOverlayWindow? shared;

    public const string Shortcut = "Ctrl+Alt+A";

    /// Moonlight's own shortcuts (Ctrl+Alt+Shift on Windows) plus ours, listed in the card.
    private static readonly (string Keys, string Action)[] Shortcuts =
    [
        ("Ctrl+Alt+Shift+Z", "Release or recapture mouse and keyboard"),
        ("Ctrl+Alt+Shift+M", "Switch mouse mode (game or desktop)"),
        ("Ctrl+Alt+Shift+X", "Fullscreen or window"),
        ("Ctrl+Alt+Shift+S", "Performance stats"),
        ("Ctrl+Alt+Shift+V", "Paste clipboard as text"),
        ("Ctrl+Alt+Shift+D", "Minimize the stream"),
        ("Ctrl+Alt+Shift+Q", "Quit the stream"),
        (Shortcut, "Show or hide this card"),
    ];

    /// Whether streams get the overlay at all (Stream → Overlay).
    public static bool Enabled
    {
        get => Preferences.Get<bool?>("overlayEnabled") ?? true;
        set => Preferences.Set("overlayEnabled", value);
    }

    public StreamOverlayModel Model { get; } = new();

    private readonly Border card;
    private readonly Border toast;
    private readonly TextBlock toastText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private readonly StackPanel rows = new() { Spacing = 8 };
    private readonly TextBlock latency = new() { FontSize = 12, FontWeight = FontWeight.Medium };
    private readonly Avalonia.Controls.Shapes.Ellipse latencyDot = new() { Width = 7, Height = 7 };
    private readonly Border congestion;
    private readonly DispatcherTimer clock;
    private readonly DispatcherTimer hotKey;
    private bool hotKeyWasDown;
    private CancellationTokenSource? intro;

    private StreamOverlayWindow()
    {
        Title = "AntCloud overlay";
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = Application.Current?.FindResource("AppFont") as FontFamily ?? FontFamily.Default;

        congestion = new Border
        {
            IsVisible = false,
            Child = Row(new StrokeIcon { Data = Icons.Warning, Width = 13, Height = 13, Foreground = Warn },
                        new TextBlock { Text = "Connection busy on the cloud PC", FontSize = 12, FontWeight = FontWeight.Medium, Foreground = Warn }),
        };

        var shortcutList = new StackPanel { Spacing = 5 };
        shortcutList.Children.Add(new TextBlock { Text = "Shortcuts", FontSize = 12, FontWeight = FontWeight.SemiBold, Classes = { "secondary" } });
        foreach (var (keys, action) in Shortcuts)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("118,*") };
            grid.Children.Add(new TextBlock { Text = keys, FontSize = 11, FontWeight = FontWeight.SemiBold });
            var text = new TextBlock { Text = action, FontSize = 11, TextWrapping = TextWrapping.Wrap, Classes = { "secondary" } };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            shortcutList.Children.Add(grid);
        }

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        header.Children.Add(new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(6), ClipToBounds = true,
            Child = new Image { Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://AntCloud/Assets/AntCloud.png"))) },
        });
        var name = new TextBlock { Text = "AntCloud", FontWeight = FontWeight.SemiBold, FontSize = 14, Margin = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 1);
        header.Children.Add(name);
        var badge = Row(latencyDot, latency);
        Grid.SetColumn(badge, 2);
        header.Children.Add(badge);

        card = Surface(new StackPanel
        {
            Spacing = 12,
            Width = 300,
            Children =
            {
                header, rows, congestion,
                Divider(),
                shortcutList,
            },
        });
        toast = Surface(Row(new StrokeIcon { Data = Icons.Warning, Width = 15, Height = 15, Foreground = Warn, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) }, toastText));
        toast.MaxWidth = 332;

        Content = new StackPanel { Spacing = 10, Margin = new Thickness(8), Children = { card, toast } };

        clock = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, (_, _) => Refresh());
        hotKey = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Input, (_, _) => PollHotKey());

        Model.PropertyChanged += (_, _) => Refresh();
        Model.Health.PropertyChanged += (_, _) => Refresh();
    }

    private static readonly IBrush Warn = new SolidColorBrush(Color.Parse("#F7A325"));

    /// A floating card. Colours follow the light or dark theme, like the main window.
    private static Border Surface(Control child)
    {
        var border = new Border
        {
            Child = child,
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BoxShadow = Motion.CardShadow("0 8 24 0 #40000000"),
        };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("OverlayCard"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("CardBorder"));
        return border;
    }

    private static Border Divider()
    {
        var line = new Border { Height = 1 };
        line.Bind(Border.BackgroundProperty, line.GetResourceObservable("DividerBrush"));
        return line;
    }

    private static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in children) { c.VerticalAlignment = c.VerticalAlignment == VerticalAlignment.Top ? VerticalAlignment.Top : VerticalAlignment.Center; row.Children.Add(c); }
        return row;
    }

    // Lifecycle

    /// Starts the overlay when a stream begins; shows the card briefly so the shortcut is discoverable.
    public void Begin(string host, UserProfile? profile)
    {
        if (Model.IsActive || !Enabled) return;
        Model.Begin(host, profile);
        clock.Start();
        if (OperatingSystem.IsWindows()) hotKey.Start();
        Model.CardVisible = true;
        intro?.Cancel();
        var ct = (intro = new CancellationTokenSource()).Token;
        _ = Task.Delay(TimeSpan.FromSeconds(6), ct).ContinueWith(t => { if (!t.IsCanceled) Model.CardVisible = false; },
                                                                  TaskScheduler.FromCurrentSynchronizationContext());
    }

    public void End()
    {
        if (!Model.IsActive) return;
        intro?.Cancel();
        clock.Stop();
        hotKey.Stop();
        Model.End();
        Hide();
    }

    public void Toggle()
    {
        intro?.Cancel();
        Model.CardVisible = !Model.CardVisible;
    }

    // Drawing

    private void Refresh()
    {
        if (!Model.IsActive) return;
        var now = DateTime.Now;
        rows.Children.Clear();
        if (Model.MinutesLeft(now) is { } left) rows.Children.Add(Line("Time left", UserProfile.Duration(left), left <= 10 ? Warn : null));
        if (Model.SessionStart is { } start) rows.Children.Add(Line("This session", UserProfile.Duration((int)(now - start).TotalMinutes)));
        if (Model.NightPlan) rows.Children.Add(Line("Night plan ends", StreamOverlayModel.NightPlanEnd(now).ToString("t")));

        latency.Text = Model.Health.Latency is { } ms ? $"{Math.Round(ms)} ms" : "…";
        latencyDot.Fill = Model.Health.Latency is { } l ? new SolidColorBrush(LatencyZones.For(l).Color) : Brushes.Gray;
        congestion.IsVisible = Model.Health.Status == HealthStatus.Congested;

        card.IsVisible = Model.CardVisible;
        toast.IsVisible = Model.Toast != null;
        toastText.Text = Model.Toast;

        if (card.IsVisible || toast.IsVisible)
        {
            if (!IsVisible) Show();
            PlaceTopRight();
        }
        else if (IsVisible)
        {
            Hide();
        }
    }

    private static Grid Line(string title, string value, IBrush? emphasis = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock { Text = title, FontSize = 13, Classes = { "secondary" } });
        var v = new TextBlock { Text = value, FontSize = 13, FontWeight = FontWeight.SemiBold, Classes = { "num" } };
        if (emphasis != null) v.Foreground = emphasis;
        Grid.SetColumn(v, 1);
        grid.Children.Add(v);
        return grid;
    }

    /// Top-right of the screen Moonlight is most likely on: the one with the mouse.
    private void PlaceTopRight()
    {
        var screen = CursorScreen() ?? Screens.Primary;
        if (screen == null) return;
        var area = screen.WorkingArea;
        var width = (int)(Math.Max(Bounds.Width, 348) * screen.Scaling);
        Position = new PixelPoint(area.Right - width - (int)(12 * screen.Scaling), area.Y + (int)(12 * screen.Scaling));
    }

    private Avalonia.Platform.Screen? CursorScreen()
    {
        if (!OperatingSystem.IsWindows() || !GetCursorPos(out var p)) return null;
        return Screens.ScreenFromPoint(new PixelPoint(p.X, p.Y));
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (OperatingSystem.IsWindows() && TryGetPlatformHandle()?.Handle is { } hwnd)
        {
            // Never activates (the stream keeps focus) and stays out of Alt+Tab.
            const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, GetWindowLongPtr(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }
    }

    // Shortcut

    /// Ctrl+Alt+A, read from the keyboard state so it works while Moonlight has the keyboard. Shift must be
    /// up, so Moonlight's own Ctrl+Alt+Shift shortcuts don't also toggle the card.
    private void PollHotKey()
    {
        const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_A = 0x41;
        static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
        var down = Down(VK_CONTROL) && Down(VK_MENU) && Down(VK_A) && !Down(VK_SHIFT);
        if (down && !hotKeyWasDown) Toggle();
        hotKeyWasDown = down;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
