using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AntCloud.Core.Session;

enum AspectRatio { Wide, Tall, Ultrawide }

static class AspectRatioExt
{
    public static string Label(this AspectRatio a) => a switch
    {
        AspectRatio.Tall => "16:10",
        AspectRatio.Ultrawide => "21:9",
        _ => "16:9",
    };

    /// Single-letter code the AntCloud server uses in the `stream` spec.
    public static string ServerCode(this AspectRatio a) => a switch
    {
        AspectRatio.Tall => "W",
        AspectRatio.Ultrawide => "U",
        _ => "N",
    };

    /// Moonlight resolution flags for a given vertical resolution, matching AntCloud's client.
    public static string[] MoonlightArgs(this AspectRatio a, string height)
    {
        if (a == AspectRatio.Wide) return [height == "2160" ? "--4K" : $"--{height}"];
        var custom = a == AspectRatio.Tall
            ? new Dictionary<string, string> { ["720"] = "1280x800", ["1080"] = "1920x1200", ["1440"] = "2560x1600", ["2160"] = "3840x2400" }
            : new Dictionary<string, string> { ["720"] = "1680x720", ["1080"] = "2560x1080", ["1440"] = "3440x1440", ["2160"] = "5120x2160" };
        return custom.TryGetValue(height, out var size) ? ["--resolution", size] : ["--1080"];
    }
}

/// Stream settings, saved on every change.
sealed class StreamSettings : ObservableObject
{
    private string resolution = "1080";
    public string Resolution { get => resolution; set => Set(ref resolution, value); }

    private string fps = "60";
    public string Fps { get => fps; set => Set(ref fps, value); }

    private string bitrate = "20000";
    public string Bitrate { get => bitrate; set => Set(ref bitrate, value); }

    private AspectRatio aspect = AspectRatio.Wide;
    public AspectRatio Aspect { get => aspect; set => Set(ref aspect, value); }

    private bool fullscreen = true;
    public bool Fullscreen { get => fullscreen; set => Set(ref fullscreen, value); }

    private bool microphone;
    public bool Microphone { get => microphone; set => Set(ref microphone, value); }

    private bool hevc = true;
    public bool Hevc { get => hevc; set => Set(ref hevc, value); }

    /// "<res> <bitrate> <fps> 10 <aspect>" — sent as the socket `stream` query and with `setstream`.
    [JsonIgnore] public string ServerSpec => $"{Resolution} {Bitrate} {Fps} 10 {Aspect.ServerCode()}";

    public string[] MoonlightStreamArgs(string host) =>
    [
        "stream", host, Config.HostApp,
        .. Aspect.MoonlightArgs(Resolution),
        "--fps", Fps,
        "--bitrate", Bitrate,
        // Borderless rather than exclusive fullscreen on Windows, so the overlay can draw above the stream.
        "--display-mode", Fullscreen ? (OperatingSystem.IsWindows() ? "borderless" : "fullscreen") : "windowed",
        "--capture-system-keys", Fullscreen ? "fullscreen" : "never",
        "--video-codec", Hevc ? "HEVC" : "H.264",
    ];

    /// "1080p · 16:9 · 60 FPS · 20 Mbps · HEVC"
    [JsonIgnore]
    public string Summary
    {
        get
        {
            var res = Resolution == "2160" ? "4K" : $"{Resolution}p";
            var mbps = (int.TryParse(Bitrate, out var b) ? b : 0) / 1000;
            return string.Join(" · ", res, Aspect.Label(), $"{Fps} FPS", $"{mbps} Mbps", Hevc ? "HEVC" : "H.264");
        }
    }

    /// Pixel size Moonlight streams at, e.g. "1920 × 1080".
    [JsonIgnore]
    public string PixelSize
    {
        get
        {
            // Same sizes as MoonlightArgs: 16:10 keeps the 16:9 width and grows taller.
            var height = int.TryParse(Resolution, out var h) ? h : 1080;
            var wide = height * 16 / 9;
            return Aspect switch
            {
                AspectRatio.Tall => $"{wide} × {wide * 10 / 16}",
                AspectRatio.Ultrawide => $"{new Dictionary<int, int> { [720] = 1680, [1080] = 2560, [1440] = 3440, [2160] = 5120 }.GetValueOrDefault(height, wide)} × {height}",
                _ => $"{wide} × {height}",
            };
        }
    }

    /// Upper bound of data use, from the bitrate (kbit/s → GB per hour).
    [JsonIgnore]
    public string GigabytesPerHour => ((int.TryParse(Bitrate, out var b) ? b : 0) * 1000.0 * 3600 / 8 / 1e9).ToString("0.#");

    private const string Key = "streamSettings";

    /// Set while deserializing, whose property setters would otherwise save a half-read copy.
    private static bool loading;

    public static StreamSettings Load()
    {
        loading = true;
        try { return Preferences.Get<StreamSettings>(Key) ?? new StreamSettings(); }
        finally { loading = false; }
    }

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetProperty(ref field, value, name)) return;
        OnPropertyChanged(nameof(ServerSpec));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(PixelSize));
        OnPropertyChanged(nameof(GigabytesPerHour));
        if (!loading) Preferences.Set(Key, this);
    }
}
