using System.Text.Json;
using AntCloud.Core.Api;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SocketIOClient;

namespace AntCloud.Core.Session;

enum Phase { Idle, Connecting, Queued, Booting, Ready, Pairing, Streaming }

sealed record Notice(string Title, string Message);

/// Pairing with and streaming from the VM's Sunshine host. Windows (and macOS, for development) drive
/// the Moonlight CLI through `MoonlightDriver`.
interface IStreamDriver
{
    /// Pairs if needed and streams until the stream ends. Call `session.MarkStreaming()` once pairing
    /// is done and `session.MarkPairing()` if it has to pair again.
    Task Run(string host, StreamSettings settings, SessionService session, CancellationToken ct);
    /// Stops anything `Run` started. Called when the session tears down.
    void Stop();
}

/// Owns one cloud-PC session: the Socket.IO connection that allocates/boots the VM, VM readiness,
/// server notices, and (with an `IStreamDriver`) the running stream. Lives on the UI thread; socket
/// callbacks are posted back to it.
sealed class SessionService : ObservableObject
{
    private Phase phase = Phase.Idle;
    public Phase Phase
    {
        get => phase;
        private set
        {
            if (!SetProperty(ref phase, value)) return;
            foreach (var name in new[] { nameof(StatusText), nameof(DetailText), nameof(IsActive), nameof(IsBusy), nameof(IsIdle), nameof(IsReady), nameof(IsStreamingOrPairing), nameof(ShowShutDown), nameof(ShutDownLabel) })
                OnPropertyChanged(name);
        }
    }

    private int? queuePosition;
    public int? QueuePosition
    {
        get => queuePosition;
        private set { if (SetProperty(ref queuePosition, value)) OnPropertyChanged(nameof(StatusText)); }
    }

    private Notice? notice;
    public Notice? Notice { get => notice; set => SetProperty(ref notice, value); }

    private string? streamHost;
    /// The cloud PC's address while a launch or stream is running (for the in-stream overlay).
    public string? StreamHost { get => streamHost; private set => SetProperty(ref streamHost, value); }

    /// Fired when the server says the token is no longer valid.
    public Action? OnSessionExpired { get; set; }

    public ApiClient Api { get; }
    private readonly IStreamDriver? driver;
    private SocketIOClient.SocketIO? socket;
    private bool intentionalDisconnect;
    private CancellationTokenSource? vmWait;
    private CancellationTokenSource? launch;
    private CancellationTokenSource? nightPlan;

    public SessionService(ApiClient api, IStreamDriver? driver = null)
    {
        Api = api;
        this.driver = driver;
    }

    public bool IsActive => Phase != Phase.Idle;
    public bool IsIdle => Phase == Phase.Idle;
    public bool IsReady => Phase == Phase.Ready;
    public bool IsBusy => Phase is Phase.Connecting or Phase.Queued or Phase.Booting or Phase.Pairing;
    public bool IsStreamingOrPairing => Phase is Phase.Pairing or Phase.Streaming;
    public bool ShowShutDown => Phase != Phase.Idle && Phase != Phase.Ready;
    public string ShutDownLabel => IsBusy ? "Cancel" : "Shut Down";

    public string StatusText => Phase switch
    {
        Phase.Idle => "Your cloud PC is off",
        Phase.Connecting => "Connecting to AntCloud…",
        Phase.Queued => QueuePosition is { } p ? $"In queue — position {p}" : "Waiting for a server…",
        Phase.Booting => "Starting your cloud PC…",
        Phase.Ready => "Cloud PC is ready",
        Phase.Pairing => "Pairing with your cloud PC…",
        _ => "Streaming",
    };

    public string DetailText => Phase switch
    {
        Phase.Idle => "Start it when you're ready to play.",
        Phase.Connecting => "Reaching the AntCloud servers.",
        Phase.Queued => "All servers are busy. Your PC starts as soon as one frees up.",
        Phase.Booting => "Windows is starting on your cloud PC.",
        Phase.Ready => "Launch the stream to start playing.",
        Phase.Pairing => "Pairing Moonlight with your cloud PC. No PIN needed.",
        _ => "Moonlight is showing your cloud PC in its own window.",
    };

    /// Screenshot mode: forces a phase without opening a session.
    public void PreviewPhase(Phase preview, int? position = null)
    {
        QueuePosition = position;
        Phase = preview;
    }

    // Session lifecycle

    /// Starts a session, but tells a night-plan user up front when it's outside the plan's hours,
    /// instead of booting the PC only for the server to refuse it later.
    public async Task StartIfAllowed(StreamSettings settings, bool nightPlan)
    {
        if (Phase != Phase.Idle) return;
        if (nightPlan)
        {
            var now = await Api.ServerTime() ?? DateTimeOffset.Now;
            if (!IsNightPlanWindow(now))
            {
                Log.Info("[session] night plan outside its hours; not starting");
                Notice = new("Night Plan Hours", "Your night plan works between 12:30 AM and 9:30 AM IST. Come back then, or switch plans for daytime play.");
                return;
            }
        }
        Start(settings, nightPlan);
    }

    private static readonly TimeZoneInfo Ist = FindIst();

    private static TimeZoneInfo FindIst()
    {
        // Windows knows India as "India Standard Time"; ICU-based systems as "Asia/Kolkata". .NET maps both,
        // but fall back to a fixed +5:30 in case the time-zone data is missing.
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromMinutes(330), "IST", "IST"); }
    }

    public static DateTimeOffset ToIst(DateTimeOffset date) => TimeZoneInfo.ConvertTime(date, Ist);

    /// Night plans can start between 12:30 AM and 9:30 AM IST.
    public static bool IsNightPlanWindow(DateTimeOffset date)
    {
        var ist = ToIst(date);
        var minutes = ist.Hour * 60 + ist.Minute;
        return minutes >= 30 && minutes < 9 * 60 + 30;
    }

    public void Start(StreamSettings settings, bool nightPlan)
    {
        if (Phase != Phase.Idle || Api.AccessToken is not { } token) return;
        intentionalDisconnect = false;
        Phase = Phase.Connecting;
        Log.Info($"[session] connecting, stream spec \"{settings.ServerSpec}\", night plan {nightPlan}");

        var client = new SocketIOClient.SocketIO(Config.SocketUrl, new SocketIOOptions
        {
            Path = "/socket.io/",
            Transport = SocketIOClient.Transport.TransportProtocol.WebSocket,
            Reconnection = false,
            ExtraHeaders = new Dictionary<string, string> { ["Origin"] = Config.Origin },
            Query = new Dictionary<string, string>
            {
                ["type"] = "client",
                ["game"] = "desktop",
                ["token"] = token,
                ["stream"] = settings.ServerSpec,
                ["connection"] = Config.ConnectionType,
            },
        });
        socket = client;

        // Every handler checks it still belongs to the current socket: a torn-down one can still deliver late events.
        void OnUi(Action action) => Dispatcher.UIThread.Post(() => { if (socket == client) action(); });

        client.OnAny((name, response) => Log.Info($"[socket] event {name} → {response}"));
        client.OnConnected += (_, _) => Log.Info("[socket] connected");

        client.On("status", response =>
        {
            var status = Read<string>(response)?.Trim().ToLowerInvariant();
            if (status == null) return;
            OnUi(() =>
            {
                if (status.StartsWith("waiting"))
                {
                    var parts = status.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    QueuePosition = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : null;
                    Phase = Phase.Queued;
                }
                else if (status == "starting")
                {
                    Phase = Phase.Booting;
                }
            });
        });

        client.On("new_connection", response =>
        {
            var json = Read<JsonElement>(response);
            var clients = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("clients", out var c) && c.TryGetInt32(out var n) ? n : 0;
            if (clients >= 2) OnUi(() => VmJoined(nightPlan));
        });

        client.On("inactive", response =>
        {
            var reason = Read<string>(response) ?? "unknown";
            OnUi(() => HandleInactive(reason));
        });

        client.On("reset", _ => OnUi(() => vmWait?.Cancel()));

        client.OnDisconnected += (_, reason) => OnUi(() =>
        {
            Log.Info($"[socket] disconnected: {reason} (intentional: {intentionalDisconnect})");
            var wasIntentional = intentionalDisconnect;
            Teardown();
            if (!wasIntentional) Notice = new("Disconnected", $"Lost connection to the AntCloud server ({reason}).");
        });

        client.OnError += (_, error) => OnUi(() =>
        {
            if (Phase != Phase.Connecting) return;
            Teardown();
            Notice = new("Connection Error", $"Could not reach the stream server: {error}");
        });

        _ = Connect(client);
    }

    private async Task Connect(SocketIOClient.SocketIO client)
    {
        try
        {
            await client.ConnectAsync();
        }
        catch (Exception e)
        {
            Log.Info($"[socket] connect failed: {e.Message}");
            Dispatcher.UIThread.Post(() =>
            {
                if (socket != client) return;
                Teardown();
                Notice = new("Connection Error", $"Could not reach the stream server: {e.Message}");
            });
        }
    }

    private static T? Read<T>(SocketIOResponse response)
    {
        try { return response.GetValue<T>(); }
        catch (Exception) { return default; }
    }

    /// Ends the session and shuts down the VM.
    public void ShutDown()
    {
        if (socket is not { } client) { Teardown(); return; }
        intentionalDisconnect = true;
        _ = CloseSocket(client);
        Teardown();
    }

    private static async Task CloseSocket(SocketIOClient.SocketIO client)
    {
        try
        {
            if (client.Connected) await client.EmitAsync("closestream");
            await client.DisconnectAsync();
        }
        catch (Exception e) { Log.Info($"[socket] close failed: {e.Message}"); }
        finally { client.Dispose(); }
    }

    /// Pushes new display settings to the VM (the server reconfigures the host resolution).
    public void ApplySettings(StreamSettings settings)
    {
        if (socket is { Connected: true } client) _ = client.EmitAsync("setstream", settings.ServerSpec);
    }

    // Streaming

    /// Fetches the VM address, applies display settings and hands off to the platform driver.
    public void LaunchStream(StreamSettings settings)
    {
        if (Phase != Phase.Ready || driver == null) return;
        Phase = Phase.Pairing;
        var cts = new CancellationTokenSource();
        launch = cts;
        _ = RunLaunch(settings, cts.Token);
    }

    private async Task RunLaunch(StreamSettings settings, CancellationToken ct)
    {
        try
        {
            var host = await Api.VmIp();
            ct.ThrowIfCancellationRequested();
            StreamHost = host;
            ApplySettings(settings);
            await driver!.Run(host, settings, this, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (!ct.IsCancellationRequested) Notice = new("Stream Error", e.Message);
        }
        if (ct.IsCancellationRequested) return;
        StreamHost = null;
        if (Phase is Phase.Pairing or Phase.Streaming) Phase = Phase.Ready;
    }

    public void MarkStreaming()
    {
        if (Phase == Phase.Pairing) Phase = Phase.Streaming;
    }

    public void MarkPairing()
    {
        if (Phase == Phase.Streaming) Phase = Phase.Pairing;
    }

    // VM readiness

    /// The VM agent joined the room. Like AntCloud's client, the stream can be launched right away;
    /// `checkVmConnectionStatus` only turns true once Moonlight has connected, at which point the server
    /// is told to start video.
    private void VmJoined(bool nightPlanActive)
    {
        if (Phase is not (Phase.Pairing or Phase.Streaming)) Phase = Phase.Ready;
        if (vmWait != null) return;
        var cts = new CancellationTokenSource();
        vmWait = cts;
        _ = WaitForVideo(nightPlanActive, cts.Token);
    }

    private async Task WaitForVideo(bool nightPlanActive, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var connected = false;
                try { connected = await Api.ConnectionStatus(); } catch (Exception) { }
                if (connected) break;
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
        catch (OperationCanceledException) { return; }
        if (ct.IsCancellationRequested || socket is not { Connected: true } client) return;
        Log.Info("[session] VM reports connected → control video");
        await client.EmitAsync("control", "video");
        if (nightPlanActive) StartNightPlanMonitor();
    }

    /// Night plans are only valid 00:30–09:35 IST; end the session once the server clock passes the cutoff.
    private void StartNightPlanMonitor()
    {
        nightPlan?.Cancel();
        var cts = new CancellationTokenSource();
        nightPlan = cts;
        _ = MonitorNightPlan(cts.Token);
    }

    private async Task MonitorNightPlan(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var now = ToIst(await Api.ServerTime() ?? DateTimeOffset.Now);
                if (now.Hour * 60 + now.Minute >= 9 * 60 + 35)
                {
                    ShutDown();
                    Notice = new("Night Plan Ended", "Your night plan session ended at 9:30 AM IST. The next window opens at 12:30 AM IST.");
                    return;
                }
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    // Server notices

    private static readonly Dictionary<string, (string Title, string Message)> InactiveMessages = new()
    {
        ["daytime"] = ("Night Plan", "Outside active hours. Come back between 12:30 AM and 9:30 AM."),
        ["emailverification"] = ("Email Verification Required", "Please verify your email address to continue using AntCloud. Check your inbox for the verification link."),
        ["dual"] = ("Multiple Connections Detected", "Another stream is already running on this account. Close the other connection and try again."),
        ["inactive"] = ("Account Disabled", "Your account has been disabled. Contact support@antcloud.co for assistance."),
        ["vmnotfound"] = ("Profile Not Ready", "Your streaming profile is still being prepared. Please wait 5 minutes and try again."),
        ["vmissue"] = ("Virtual Machine Error", "Failed to launch your virtual machine. Please wait 2–3 minutes and try again."),
        ["serversfull"] = ("Servers at Capacity", "All servers are currently busy. Please wait 5–10 minutes and try again."),
        ["wrongToken"] = ("Session Expired", "Your session has expired. Please log in again."),
        ["planendwarning"] = ("Plan Expiring Soon", "Your plan will expire soon. Renew to avoid interruption."),
        ["planover"] = ("Plan Expired", "Your plan has expired. Renew to continue using AntCloud."),
    };

    private void HandleInactive(string reason)
    {
        if (reason == "videonotstarting")
        {
            ShutDown();
            Notice = new("Stream Failed", "The VM's video did not start. Please try again.");
            return;
        }

        var (title, message) = InactiveMessages.TryGetValue(reason, out var m) ? m : ("Connection Error", $"An unexpected error occurred: {reason}");
        Notice = new(title, message);

        switch (reason)
        {
            case "wrongToken" or "inactive":
                ShutDown();
                OnSessionExpired?.Invoke();
                break;
            case "planover":
                ShutDown();
                break;
        }
    }

    private void Teardown()
    {
        if (Phase != Phase.Idle) Log.Info($"[session] teardown from {Phase}");
        vmWait?.Cancel();
        vmWait = null;
        launch?.Cancel();
        launch = null;
        nightPlan?.Cancel();
        nightPlan = null;
        driver?.Stop();
        socket = null;
        StreamHost = null;
        QueuePosition = null;
        Phase = Phase.Idle;
    }
}
