using System.Diagnostics;
using System.Net.Sockets;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AntCloud.Core.Session;

enum CheckStage { Idle, Latency, Download, Done, Failed }

/// Estimates how a stream would feel without starting the cloud PC.
///
/// Latency is the TCP handshake time to AntCloud's socket server. It sits in the same network
/// (AS "ANTPLAY", 103.182.64.0/23) as the cloud PCs, so its round trip is a close stand-in for theirs.
/// AntCloud has no speed-test endpoint, so download speed is measured against Cloudflare's public one:
/// that's your internet connection, not AntCloud's.
sealed class NetworkCheck : ObservableObject
{
    private CheckStage stage = CheckStage.Idle;
    public CheckStage Stage
    {
        get => stage;
        private set
        {
            if (!SetProperty(ref stage, value)) return;
            OnPropertyChanged(nameof(IsRunning));
        }
    }

    public string? FailureMessage { get; private set; }

    /// Round-trip samples in milliseconds, in the order they were measured.
    public List<double> Samples { get; } = [];
    public int Lost { get; private set; }
    public double? DownloadMbps { get; private set; }
    /// Live download speed while the download runs.
    public double LiveMbps { get; private set; }

    /// Raised whenever a sample or a speed reading arrives.
    public event Action? Updated;

    public const int SampleCount = 20;
    public const string LatencyHost = "socket.antcloud.co";
    public const int LatencyPort = 8000;
    private const string DownloadUrl = "https://speed.cloudflare.com/__down?bytes=50000000";

    public bool IsRunning => Stage is CheckStage.Latency or CheckStage.Download;

    // Results

    public double? MedianLatency
    {
        get
        {
            if (Samples.Count == 0) return null;
            var sorted = Samples.Order().ToList();
            return sorted[sorted.Count / 2];
        }
    }

    /// Mean difference between consecutive samples, the usual definition of jitter.
    public double? Jitter => Samples.Count > 1 ? Samples.Zip(Samples.Skip(1), (a, b) => Math.Abs(b - a)).Average() : null;

    public double LossPercent => Samples.Count + Lost == 0 ? 0 : (double)Lost / (Samples.Count + Lost) * 100;

    /// The bitrate (kbps) a stream at this resolution and frame rate needs, capped so it leaves headroom on
    /// the measured connection. A faster line doesn't make 1080p look better past what it needs; it only
    /// adds stutter risk on Wi-Fi.
    public int? SuggestedBitrate(string resolution, string fps)
    {
        if (DownloadMbps is not { } mbps) return null;
        var at60 = resolution switch { "720" => 10.0, "1440" => 40, "2160" => 50, _ => 20 };
        var needed = at60 * (fps == "30" ? 0.6 : fps == "120" ? 1.5 : 1);
        int[] steps = [5, 10, 20, 30, 40, 50];
        var target = steps.FirstOrDefault(s => s >= needed, 50);
        var affordable = steps.LastOrDefault(s => s <= mbps * 0.6, 5);
        return Math.Min(target, affordable) * 1000;
    }

    // Running

    public async Task Run()
    {
        if (IsRunning) return;
        Samples.Clear();
        Lost = 0;
        DownloadMbps = null;
        LiveMbps = 0;
        FailureMessage = null;

        Stage = CheckStage.Latency;
        Updated?.Invoke();
        _ = await HandshakeTime(); // warm-up: DNS lookup and route setup
        for (var i = 0; i < SampleCount; i++)
        {
            if (await HandshakeTime() is { } ms) Samples.Add(ms); else Lost++;
            Updated?.Invoke();
            await Task.Delay(120);
        }
        if (Samples.Count == 0)
        {
            FailureMessage = "Couldn't reach AntCloud's servers. Check your internet connection.";
            Stage = CheckStage.Failed;
            return;
        }

        Stage = CheckStage.Download;
        DownloadMbps = await MeasureDownload(mbps => Dispatcher.UIThread.Post(() => { LiveMbps = mbps; Updated?.Invoke(); }));
        Stage = CheckStage.Done;
        Updated?.Invoke();
        Log.Info($"[network] latency {MedianLatency:0.0} ms, jitter {Jitter:0.0} ms, loss {Lost}, download {DownloadMbps:0.0} Mbps");
    }

    /// Time from starting a TCP connection to it being ready: one network round trip. Defaults to
    /// AntCloud's socket server; the stream monitor points it at the cloud PC.
    public static async Task<double?> HandshakeTime(string host = LatencyHost, int port = LatencyPort)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var client = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            // Resolve first, so the measurement is the handshake alone.
            var addresses = await System.Net.Dns.GetHostAddressesAsync(host, cts.Token);
            var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
            if (address == null) return null;
            var clock = Stopwatch.StartNew();
            await client.ConnectAsync(address, port, cts.Token);
            return clock.Elapsed.TotalMilliseconds;
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException)
        {
            return null;
        }
    }

    /// Downloads for up to 8 seconds and returns the average speed after the first half second (which is
    /// mostly connection setup and TCP ramp-up).
    private static async Task<double?> MeasureDownload(Action<double> progress)
    {
        using var http = new HttpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        long bytesAfterWarmup = 0;
        Stopwatch? sinceWarmup = null;
        var clock = Stopwatch.StartNew();
        try
        {
            using var res = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            await using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
            var buffer = new byte[64 * 1024];
            var lastReport = TimeSpan.Zero;
            int read;
            while ((read = await stream.ReadAsync(buffer, cts.Token)) > 0)
            {
                if (sinceWarmup == null)
                {
                    if (clock.Elapsed.TotalSeconds >= 0.5) sinceWarmup = Stopwatch.StartNew();
                    continue;
                }
                bytesAfterWarmup += read;
                var elapsed = sinceWarmup.Elapsed;
                if (elapsed.TotalSeconds > 0.2 && elapsed - lastReport > TimeSpan.FromMilliseconds(150))
                {
                    lastReport = elapsed;
                    progress(bytesAfterWarmup * 8 / elapsed.TotalSeconds / 1_000_000);
                }
            }
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException) { }
        if (sinceWarmup == null || bytesAfterWarmup == 0) return null;
        return bytesAfterWarmup * 8 / sinceWarmup.Elapsed.TotalSeconds / 1_000_000;
    }
}
