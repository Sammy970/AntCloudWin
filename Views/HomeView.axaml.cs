using AntCloud.Controls;
using AntCloud.Core;
using AntCloud.Core.Session;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace AntCloud.Views;

partial class HomeView : UserControl
{
    private readonly HomeModel model;
    private readonly Dictionary<string, (string Title, Func<Control> Make)> panes;
    private readonly Dictionary<string, Control> made = [];

    public HomeView() : this(new HomeModel(new Core.Auth.AuthStore())) { }

    public HomeView(HomeModel model)
    {
        this.model = model;
        DataContext = model;
        InitializeComponent();

        panes = new()
        {
            ["cloud"] = ("Cloud PC", () => new CloudPcPane(model, () => Select("stream"))),
            ["stream"] = ("Stream", () => new StreamPane(model)),
            ["network"] = ("Network", () => new NetworkPane(model)),
            ["controllers"] = ("Controllers", () => new ControllersPane()),
            ["about"] = ("About", () => new AboutPane()),
        };
        Nav.SelectionChanged += (_, _) => Show();
        // AlwaysSelected picks the first item while loading, before this handler exists.
        Nav.SelectedIndex = 0;
        Show();

        model.Auth.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.Auth.Profile)) ShowProfile(); };
        model.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionService.Phase)) PhaseChanged();
            if (e.PropertyName == nameof(SessionService.Notice) && model.Session.Notice is { } notice)
            {
                model.Session.Notice = null;
                MainWindow.ShowNotice(this, notice.Title, notice.Message);
            }
        };
        ShowProfile();
        PhaseChanged();
    }

    public void Select(string pane)
    {
        foreach (var item in Nav.Items.OfType<ListBoxItem>())
            if (item.Tag as string == pane) Nav.SelectedItem = item;
    }

    /// <summary>Screenshot mode: the Controllers pane with a sample controller mid-press.</summary>
    public void ShowControllerSample()
    {
        Select("controllers");
        if (made.TryGetValue("controllers", out var pane) && pane is ControllersPane controllers) controllers.ShowSample();
    }

    private void Show()
    {
        if (Nav.SelectedItem is not ListBoxItem { Tag: string key }) return;
        var (title, make) = panes[key];
        PageTitle.Text = title;
        if (!made.TryGetValue(key, out var pane)) made[key] = pane = make();
        PaneHost.Content = pane;
    }

    private void ShowProfile()
    {
        var profile = model.Auth.Profile;
        Initial.Text = profile?.Initial ?? "";
        ProfileName.Text = profile?.FirstName ?? "";
        Plan.Text = profile?.PlanName ?? "";
        PlanIcon.Data = profile?.IsNightPlan == true ? Icons.Moon : Icons.Star;
        EmailItem.Header = profile?.Email ?? "";

        Usage.IsVisible = profile?.MinutesLeft != null;
        if (profile?.MinutesLeft is { } left)
        {
            TimeLeft.Text = Core.Api.UserProfile.Duration(left);
            TimeBar.Value = (double)left / Math.Max(profile.TotalTimeMonth, 1);
            Renews.Text = profile.RenewText;
            Renews.IsVisible = profile.DaysLeft != null;
        }
    }

    private void PhaseChanged()
    {
        var phase = model.Session.Phase;
        // Dot next to "Cloud PC": green when ready or streaming, orange while busy, none when off.
        CloudNav.Dot = phase switch
        {
            Phase.Idle => null,
            Phase.Ready or Phase.Streaming => this.FindResource("GoodBrush") as IBrush,
            _ => this.FindResource("WarnBrush") as IBrush,
        };
        CloudNav.DotTip = model.Session.StatusText;

        // The in-stream overlay (time left, health warnings, Ctrl+Alt+A) lives exactly as long as the stream.
        if (phase == Phase.Streaming && model.Session.StreamHost is { } host)
            StreamOverlayWindow.Shared.Begin(host, model.Auth.Profile);
        else if (phase != Phase.Streaming)
            StreamOverlayWindow.Shared.End();
    }

    private async void RefreshAccount(object? sender, RoutedEventArgs e) => await model.Auth.ReloadProfile();

    private void OpenPlans(object? sender, RoutedEventArgs e) => HomeModel.Open(Config.PricingUrl);

    private void SignOut(object? sender, RoutedEventArgs e)
    {
        model.Session.ShutDown();
        model.Auth.Logout();
    }
}

/// A sidebar row: the selection pill, an icon, the label and an optional status dot.
sealed class NavItem : Grid
{
    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<NavItem, Geometry?>(nameof(Icon));
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<NavItem, string>(nameof(Text), "");

    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    private readonly StrokeIcon glyph = new() { Width = 16, Height = 16, Margin = new Thickness(13, 0, 14, 0), StrokeWidth = 1.8 };
    private readonly TextBlock label = new() { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontSize = 14 };
    private readonly Avalonia.Controls.Shapes.Ellipse dot = new() { Width = 7, Height = 7, Margin = new Thickness(0, 0, 12, 0), IsVisible = false };

    public NavItem()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto");
        Height = 36;
        var pill = new Border
        {
            Width = 3,
            Height = 16,
            CornerRadius = new CornerRadius(1.5),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        pill.Classes.Add("pill");
        pill.Bind(Border.BackgroundProperty, pill.GetResourceObservable("AntBlue"));
        Children.Add(pill);
        SetColumn(glyph, 1);
        glyph.Bind(StrokeIcon.ForegroundProperty, glyph.GetResourceObservable("TextPrimary"));
        Children.Add(glyph);
        SetColumn(label, 2);
        Children.Add(label);
        SetColumn(dot, 3);
        Children.Add(dot);
    }

    public IBrush? Dot
    {
        set
        {
            dot.Fill = value;
            dot.IsVisible = value != null;
        }
    }

    public string DotTip { set => ToolTip.SetTip(dot, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconProperty) glyph.Data = Icon;
        if (change.Property == TextProperty) label.Text = Text;
    }
}
