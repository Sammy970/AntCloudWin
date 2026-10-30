using AntCloud.Core.Api;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AntCloud.Core.Session;

enum HealthStatus { Measuring, Good, Congested }

/// Watches latency to the cloud PC during a stream and flags when it climbs well above that stream's own
/// baseline. Measured 2026-10-30: an Epic download on the cloud PC took the stream from 40 ms to 88 ms
/// (spikes to 343 ms) while AntCloud's servers stayed at 40 ms, so a jump like that almost always means
/// something on the cloud PC is filling its connection.
sealed class StreamHealth : ObservableObject
{
    private double? latency;
    /// Median of the last few samples, in ms.
    public double? Latency { get => latency; private set => SetProperty(ref latency, value); }

    private HealthStatus status = HealthStatus.Measuring;
    public HealthStatus Status { get => status; private set => SetProperty(ref status, value); }

    /// Called once per congestion episode (and at most every two minutes) with the latency.
    public Action<double>? OnCongestion { get; set; }

    /// Sunshine's HTTP port on the cloud PC; a TCP handshake to it is one round trip.
    private const int ProbePort = 47989;
    private CancellationTokenSource? cts;
    private readonly List<double> recent = [];
    private double? baseline;
    private DateTime? lastWarning;

    public void Start(string host)
    {
        Stop();
        var token = (cts = new CancellationTokenSource()).Token;
        _ = Probe(host, token);
    }

    private async Task Probe(string host, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (await NetworkCheck.HandshakeTime(host, ProbePort) is { } ms && !ct.IsCancellationRequested) Record(ms);
                await Task.Delay(TimeSpan.FromSeconds(4), ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Stop()
    {
        cts?.Cancel();
        cts = null;
        recent.Clear();
        baseline = null;
        Latency = null;
        Status = HealthStatus.Measuring;
    }

    private void Record(double ms)
    {
        recent.Add(ms);
        if (recent.Count > 5) recent.RemoveAt(0);
        var median = recent.Order().ElementAt(recent.Count / 2);
        Latency = median;
        baseline = Math.Min(baseline ?? ms, ms);
        if (recent.Count < 3) return;

        switch (Status)
        {
            case HealthStatus.Measuring or HealthStatus.Good:
                if (median > baseline + 35 && median > 75)
                {
                    Status = HealthStatus.Congested;
                    if (lastWarning is not { } last || (DateTime.Now - last).TotalSeconds > 120)
                    {
                        lastWarning = DateTime.Now;
                        Log.Info($"[health] congested: {median:0} ms (baseline {baseline:0} ms)");
                        OnCongestion?.Invoke(median);
                    }
                }
                else
                {
                    Status = HealthStatus.Good;
                }
                break;
            case HealthStatus.Congested:
                if (median < baseline + 20) Status = HealthStatus.Good;
                break;
        }
    }
}

/// What the in-stream overlay shows: time left, session length, night-plan end, health and toasts.
sealed class StreamOverlayModel : ObservableObject
{
    private bool cardVisible;
    public bool CardVisible { get => cardVisible; set => SetProperty(ref cardVisible, value); }

    private string? toast;
    public string? Toast { get => toast; private set => SetProperty(ref toast, value); }

    private DateTime? sessionStart;
    public DateTime? SessionStart { get => sessionStart; private set => SetProperty(ref sessionStart, value); }

    public bool NightPlan { get; private set; }
    public StreamHealth Health { get; } = new();

    /// Plan minutes left when the stream began; the overlay counts down from there.
    private int? startMinutesLeft;
    private CancellationTokenSource? toastCts;
    private CancellationTokenSource? clockCts;

    public bool IsActive => SessionStart != null;

    public void Begin(string host, UserProfile? profile)
    {
        SessionStart = DateTime.Now;
        startMinutesLeft = profile?.MinutesLeft;
        NightPlan = profile?.IsNightPlan ?? false;
        Health.OnCongestion = ms => ShowToast($"Your cloud PC's connection is busy ({ms:0} ms). Something on it, like a game download, may be using the bandwidth.");
        Health.Start(host);

        // One reminder when plan time runs low.
        clockCts?.Cancel();
        var ct = (clockCts = new CancellationTokenSource()).Token;
        _ = RemindLowTime(ct);
    }

    private async Task RemindLowTime(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                if (MinutesLeft(DateTime.Now) is { } left && left <= 10)
                {
                    ShowToast($"{left} minute{(left == 1 ? "" : "s")} of plan time left.");
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    public void End()
    {
        Health.Stop();
        clockCts?.Cancel();
        toastCts?.Cancel();
        SessionStart = null;
        CardVisible = false;
        Toast = null;
    }

    public int? MinutesLeft(DateTime date) =>
        SessionStart is { } start && startMinutesLeft is { } left ? Math.Max(0, left - (int)(date - start).TotalMinutes) : null;

    public void ShowToast(string text, double seconds = 8)
    {
        toastCts?.Cancel();
        Toast = text;
        var ct = (toastCts = new CancellationTokenSource()).Token;
        _ = Task.Delay(TimeSpan.FromSeconds(seconds), ct).ContinueWith(t =>
        {
            if (!t.IsCanceled) Toast = null;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// The next 9:30 AM IST, when night plans end, in local time.
    public static DateTime NightPlanEnd(DateTime after)
    {
        var ist = SessionService.ToIst(after);
        var end = new DateTimeOffset(ist.Year, ist.Month, ist.Day, 9, 30, 0, ist.Offset);
        if (end <= ist) end = end.AddDays(1);
        return end.LocalDateTime;
    }
}
