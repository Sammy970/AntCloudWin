using AntCloud.Controls;
using AntCloud.Core.Session;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AntCloud.Views;

partial class StreamPane : UserControl
{
    private readonly HomeModel model;

    public StreamPane() : this(new HomeModel(new Core.Auth.AuthStore())) { }

    public StreamPane(HomeModel model)
    {
        this.model = model;
        DataContext = model;
        InitializeComponent();
        var settings = model.Settings;

        Aspect.SelectedValue = settings.Aspect.ToString();
        Aspect.PropertyChanged += (_, e) =>
        {
            if (e.Property == Segmented.SelectedValueProperty && Enum.TryParse<AspectRatio>(Aspect.SelectedValue, out var aspect)) settings.Aspect = aspect;
        };

        Bitrate.SelectedItem = Bitrate.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag as string == settings.Bitrate);
        Bitrate.SelectionChanged += (_, _) => { if (Bitrate.SelectedItem is ComboBoxItem { Tag: string kbps }) settings.Bitrate = kbps; };
        // The network check can change the bitrate too.
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StreamSettings.Bitrate))
                Bitrate.SelectedItem = Bitrate.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag as string == settings.Bitrate);
        };

        Overlay.IsChecked = StreamOverlayWindow.Enabled;
        Overlay.IsCheckedChanged += (_, _) => StreamOverlayWindow.Enabled = Overlay.IsChecked == true;

        SkipNightPlan.IsChecked = SessionService.NightPlanMonitorDisabled;
        SkipNightPlan.IsCheckedChanged += (_, _) => SessionService.NightPlanMonitorDisabled = SkipNightPlan.IsChecked == true;
    }
}

/// One settings row: icon, title and description on the left, its control (the content) on the right.
/// Drawn by its template in Styles.axaml.
sealed class SettingRow : ContentControl
{
    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<SettingRow, Geometry?>(nameof(Icon));
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<SettingRow, string>(nameof(Title), "");
    public static readonly StyledProperty<string?> DetailProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(Detail));

    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Detail { get => GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
}
