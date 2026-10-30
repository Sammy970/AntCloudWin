using AntCloud.Core;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AntCloud.Views;

partial class CloudPcPane : UserControl
{
    private readonly HomeModel model;
    private readonly Action editSettings;

    public CloudPcPane() : this(new HomeModel(new Core.Auth.AuthStore()), () => { }) { }

    public CloudPcPane(HomeModel model, Action editSettings)
    {
        this.model = model;
        this.editSettings = editSettings;
        DataContext = model;
        InitializeComponent();

        var installed = HomeModel.MoonlightInstalled;
        MoonlightMissing.IsVisible = !installed;
        StartButton.IsEnabled = installed;
        MoonlightHint.Text = OperatingSystem.IsWindows()
            ? "AntCloud ships with Moonlight, but it's missing from AntCloud's folder. Reinstall AntCloud, or install Moonlight yourself."
            : "Install Moonlight, then reopen AntCloud.";
    }

    private async void Start(object? sender, RoutedEventArgs e)
    {
        if (model.Auth.Profile is not { HasPaidPlan: true } profile)
        {
            MainWindow.ShowNotice(this, "Plan Required", "Please buy a plan to continue.");
            HomeModel.Open(Config.PricingUrl);
            return;
        }
        await model.Session.StartIfAllowed(model.Settings, profile.IsNightPlan);
    }

    private void Launch(object? sender, RoutedEventArgs e) => model.Session.LaunchStream(model.Settings);

    private void ApplySettings(object? sender, RoutedEventArgs e) => model.Session.ApplySettings(model.Settings);

    private void ShutDown(object? sender, RoutedEventArgs e) => model.Session.ShutDown();

    private void EditSettings(object? sender, RoutedEventArgs e) => editSettings();

    private void GetMoonlight(object? sender, RoutedEventArgs e) => HomeModel.Open(Config.MoonlightUrl);
}
