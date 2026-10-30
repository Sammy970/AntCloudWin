using AntCloud.Controls;
using AntCloud.Core.Session;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace AntCloud.Views;

partial class NetworkPane : UserControl
{
    private readonly HomeModel model;
    private NetworkCheck Check => model.Network;

    public NetworkPane() : this(new HomeModel(new Core.Auth.AuthStore())) { }

    public NetworkPane(HomeModel model)
    {
        this.model = model;
        InitializeComponent();
        Check.Updated += Refresh;
        Check.PropertyChanged += (_, _) => Refresh();
        model.Session.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SessionService.Phase)) Refresh(); };
        model.Settings.PropertyChanged += (_, _) => Refresh();
        Refresh();

        // Screenshot mode (`ANTCLOUD_PREVIEW=…:network`) runs the test on its own.
        if (Environment.GetEnvironmentVariable("ANTCLOUD_PREVIEW")?.EndsWith(":network") == true) _ = Check.Run();
    }

    /// Testing while streaming would compete with the stream for bandwidth.
    private bool Busy => model.Session.IsStreamingOrPairing;

    private void Refresh()
    {
        var median = Check.MedianLatency;
        Median.Text = median is { } m ? $"{Math.Round(m)} ms" : "";
        if (median is { } value) Median.Foreground = new SolidColorBrush(LatencyZones.For(value).Color);
        Bars.Samples = Check.Samples.ToList();
        Scale.Value = median;

        Jitter.Text = Check.Jitter is { } j ? $"{Math.Round(j)} ms" : "–";
        Loss.Text = Check.Samples.Count == 0 ? "–" : $"{Math.Round(Check.LossPercent)}%";
        Download.Text = Check.DownloadMbps is { } d ? $"{Math.Round(d)} Mbps"
            : Check.Stage == CheckStage.Download ? $"{Math.Round(Check.LiveMbps)} Mbps" : "–";

        Failure.IsVisible = Check.Stage == CheckStage.Failed;
        FailureText.Text = Check.FailureMessage;

        Run.IsEnabled = !Check.IsRunning && !Busy;
        RunText.Text = Check.Stage == CheckStage.Idle ? "Run Test" : "Run Again";
        Running.IsVisible = Check.IsRunning;
        RunningText.Text = Check.Stage == CheckStage.Latency ? "Measuring latency…" : "Measuring download speed…";
        BusyText.IsVisible = !Check.IsRunning && Busy;

        ShowVerdict();
    }

    private void ShowVerdict()
    {
        Verdict.IsVisible = Check.Stage == CheckStage.Done && Check.MedianLatency != null;
        if (!Verdict.IsVisible || Check.MedianLatency is not { } median) return;
        var zone = LatencyZones.For(median);
        var brush = new SolidColorBrush(zone.Color);
        VerdictIcon.Data = zone.Icon;
        VerdictIcon.Foreground = brush;
        VerdictTitle.Text = zone.Verdict;
        VerdictTitle.Foreground = brush;
        Advice.Text = AdviceFor(median);

        var settings = model.Settings;
        var bitrate = int.TryParse(settings.Bitrate, out var b) ? b : 0;
        if (Check.SuggestedBitrate(settings.Resolution, settings.Fps) is { } suggested)
        {
            Suggestion.IsVisible = true;
            var res = settings.Resolution == "2160" ? "4K" : settings.Resolution + "p";
            SuggestedText.Text = $"Suggested for {res} at {settings.Fps} FPS: {suggested / 1000} Mbps";
            CurrentText.Text = bitrate == suggested ? "That's what you're using." : $"You're using {bitrate / 1000} Mbps.";
            UseSuggested.IsVisible = bitrate != suggested;
            UseSuggested.Content = $"Use {suggested / 1000} Mbps";
            UseSuggested.Tag = suggested;
        }
        else
        {
            Suggestion.IsVisible = false;
        }
    }

    private string AdviceFor(double median)
    {
        var parts = new List<string> { LatencyZones.For(median).Detail };
        if (Check.Jitter is { } jitter && jitter > 12)
            parts.Add($"Latency is jumping around ({jitter:0} ms jitter); a wired connection or 5 GHz Wi-Fi closer to the router usually steadies it.");
        if (Check.LossPercent >= 5)
            parts.Add("Some attempts got no answer, which shows up as stutters.");
        if (Check.DownloadMbps is { } mbps && mbps < 15)
            parts.Add("Your download speed is low for streaming; 720p at 10 Mbps will be smoother than 1080p.");
        return string.Join(" ", parts);
    }

    private async void RunClicked(object? sender, RoutedEventArgs e) => await Check.Run();

    private void ApplySuggested(object? sender, RoutedEventArgs e)
    {
        if (UseSuggested.Tag is int kbps) model.Settings.Bitrate = kbps.ToString();
    }
}
