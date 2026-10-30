using System.Diagnostics;
using AntCloud.Core.Api;
using AntCloud.Core.Session;

namespace AntCloud.Core.Stream;

/// Pairs and streams through the Moonlight CLI and optionally forwards the mic.
sealed class MoonlightDriver : IStreamDriver
{
    private readonly MoonlightRunner moonlight = new();
    private IDisposable? mic;

    public async Task Run(string host, StreamSettings settings, SessionService session, CancellationToken ct)
    {
        if (settings.Microphone)
        {
            if (OperatingSystem.IsWindows())
            {
                try { mic = MicForwarder.Start(host); }
                catch (Exception e) { session.Notice = new("Microphone", e.Message); }
            }
            else
            {
                Log.Info("[mic] forwarding is only built for Windows");
            }
        }

        try
        {
            var skippedPairing = MoonlightRunner.IsPaired(host);
            if (skippedPairing) Log.Info($"[moonlight] {host} already paired, skipping pair");
            else await Pair(host, session.Api, ct);
            ct.ThrowIfCancellationRequested();

            session.MarkStreaming();
            var args = settings.MoonlightStreamArgs(host);
            Log.Info($"[moonlight] {string.Join(' ', args)}");
            var started = Stopwatch.StartNew();
            var status = await moonlight.Stream(args, ct);
            Log.Info($"[moonlight] stream exited with {status}");

            // A stale saved pairing (e.g. the VM was re-provisioned) fails fast — pair and retry once.
            if (skippedPairing && status != 0 && started.Elapsed.TotalSeconds < 15 && !ct.IsCancellationRequested)
            {
                Log.Info("[moonlight] stream failed quickly, re-pairing");
                session.MarkPairing();
                await Pair(host, session.Api, ct);
                session.MarkStreaming();
                status = await moonlight.Stream(args, ct);
                Log.Info($"[moonlight] stream exited with {status}");
            }
        }
        finally
        {
            StopMic();
        }
    }

    public void Stop()
    {
        moonlight.Terminate();
        StopMic();
    }

    private void StopMic()
    {
        mic?.Dispose();
        mic = null;
    }

    /// Starts `Moonlight pair` with a random PIN and, in parallel, submits that PIN to the backend until it
    /// reports Sunshine accepted it — the same handshake AntCloud's client uses.
    private async Task Pair(string host, ApiClient api, CancellationToken ct)
    {
        var pin = Random.Shared.Next(1000, 10000).ToString();
        using var submit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = SubmitPin(api, pin, submit.Token);
        Log.Info($"[moonlight] pair {host}");
        var status = await moonlight.Pair(host, pin, ct);
        submit.Cancel();
        Log.Info($"[moonlight] pair exited with {status} (non-zero is expected if already paired)");
    }

    private static async Task SubmitPin(ApiClient api, string pin, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            while (!ct.IsCancellationRequested)
            {
                try { if (await api.VmAuth(pin)) return; } catch (Exception e) when (e is not OperationCanceledException) { }
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
        catch (OperationCanceledException) { }
    }
}
