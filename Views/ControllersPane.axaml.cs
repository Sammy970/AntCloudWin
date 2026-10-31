using AntCloud.Controls;
using AntCloud.Core;
using AntCloud.Core.Controllers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace AntCloud.Views;

partial class ControllersPane : UserControl
{
    private readonly DispatcherTimer poll;
    private readonly DispatcherTimer listTimer;
    private List<ConnectedController> controllers = [];
    private PadState state = PadState.Rest;
    private bool sample;

    public ControllersPane()
    {
        InitializeComponent();
        // Live input sixty times a second while the pane is on screen; the device list every two seconds.
        poll = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) => Poll());
        listTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => RefreshList());
        Picker.SelectionChanged += (_, _) => Show();
        AttachedToVisualTree += (_, _) => { RefreshList(); poll.Start(); listTimer.Start(); };
        DetachedFromVisualTree += (_, _) => { poll.Stop(); listTimer.Stop(); };
    }

    /// <summary>Screenshot mode: a sample Xbox controller mid-press, without any hardware.</summary>
    public void ShowSample()
    {
        sample = true;
        controllers = [new ConnectedController("sample", "Xbox Wireless Controller", 0x045E, 0x02E0, "Bluetooth", "Battery full", PadStyle.Xbox, 0, true, "Xbox One Controller")];
        state = new PadState(new HashSet<PadButton> { PadButton.A, PadButton.RightShoulder }, 0.55, 0.35, 0, 0, 0, 0.7);
        Show();
    }

    private ConnectedController? Selected => controllers.ElementAtOrDefault(Math.Max(0, Picker.SelectedIndex));

    private void RefreshList()
    {
        if (sample || !OperatingSystem.IsWindows()) { Show(); return; }
        var list = Gamepads.List();
        if (list.Select(c => c.Key).SequenceEqual(controllers.Select(c => c.Key)) && list.SequenceEqual(controllers)) return;
        var selectedKey = Selected?.Key;
        controllers = list;
        Picker.ItemsSource = list.Select(c => c.Name).ToList();
        Picker.SelectedIndex = Math.Max(0, list.FindIndex(c => c.Key == selectedKey));
        Log.Info($"[controllers] {string.Join(", ", list.Select(c => $"{c.Name} [{c.Ids ?? "no ID"}] {(c.Ready ? "ready" : "unknown")}"))}");
        Show();
    }

    private void Poll()
    {
        if (sample || !OperatingSystem.IsWindows() || Selected is not { Slot: { } slot }) return;
        if (Gamepads.State(slot) is { } next && !next.Same(state))
        {
            state = next;
            ShowState();
        }
    }

    private void Show()
    {
        var c = Selected;
        Picker.IsVisible = controllers.Count > 1;
        EmptyCard.IsVisible = c == null;
        ControllerCard.IsVisible = c != null;
        ShowOthers(c);
        if (c == null) return;

        ControllerName.Text = c.Name;
        ControllerDetails.Text = string.Join(" · ", new[] { c.Ids, c.Connection, c.Battery }.Where(s => s != null));
        var color = c.Ready ? Color.Parse("#2EB85C") : Color.Parse("#F7A325");
        Verdict.Background = new SolidColorBrush(color, 0.15);
        VerdictIcon.Data = c.Ready ? Icons.CheckCircle : Icons.Warning;
        VerdictIcon.Foreground = new SolidColorBrush(color);
        VerdictText.Text = c.Ready ? "Ready to play" : "May not work";
        VerdictText.Foreground = VerdictIcon.Foreground;
        ToolTip.SetTip(Verdict, c.Ready ? "Moonlight recognises this controller, so it'll work on your cloud PC." : null);
        Advice.IsVisible = !c.Ready;

        var live = c.Slot != null;
        Drawing.PadStyle = c.Style;
        Drawing.Dimmed = !live;
        NoLive.IsVisible = !live;
        Readout.IsVisible = live;
        Vibrate.IsVisible = live;
        if (!live) state = PadState.Rest;
        ShowState();
    }

    private void ShowState()
    {
        Drawing.State = state;
        LeftStick.Text = $"{state.LX:+0.00;-0.00}  {state.LY:+0.00;-0.00}";
        RightStick.Text = $"{state.RX:+0.00;-0.00}  {state.RY:+0.00;-0.00}";
        Triggers.Text = $"{state.LT:0.00}  {state.RT:0.00}";
    }

    private void ShowOthers(ConnectedController? selected)
    {
        while (Others.Children.Count > 1) Others.Children.RemoveAt(1);
        var others = controllers.Where(c => c != selected).ToList();
        OthersCard.IsVisible = others.Count > 0;
        foreach (var c in others)
        {
            var row = new StackPanel { Spacing = 1 };
            row.Children.Add(new TextBlock { Text = c.Name, FontWeight = FontWeight.Medium });
            row.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", new[] { c.Ids, c.Connection, c.Ready ? "Ready to play" : "May not work" }.Where(s => s != null)),
                Classes = { "caption", "secondary" },
            });
            Others.Children.Add(row);
        }
    }

    private async void VibrateClicked(object? sender, RoutedEventArgs e)
    {
        if (OperatingSystem.IsWindows() && Selected is { Slot: { } slot }) await Gamepads.Vibrate(slot);
    }

    private void OpenBluetooth(object? sender, RoutedEventArgs e) => HomeModel.Open("ms-settings:bluetooth");
}
