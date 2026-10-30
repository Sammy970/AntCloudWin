using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace AntCloud.Core.Api;

sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// Thin client for https://api.antcloud.co/api. Responses are loosely shaped, so JSON is handled as
/// `JsonNode` rather than strict models.
///
/// Routes match AntCloud's Windows client v0.016 (`Config.MatchedClientVersion`); that release renamed
/// every route, see re/FINDINGS.md. A route AntCloud removes answers Express's "Cannot GET /api/…" 404,
/// which `Send` reports as a service change rather than a plain error.
sealed class ApiClient
{
    public string? AccessToken { get; set; }
    /// Called once on a 401 to obtain a new access token; returns null if refresh failed.
    public Func<Task<string?>>? RefreshHandler { get; set; }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// Requests whose bodies (tokens, profile) are never logged.
    private static readonly string[] AuthPaths = ["login", "phoneLogin", "phoneNumber/", "myself", "users/"];

    // Auth

    public async Task<(string Access, string? Refresh)> Login(string email, string password) =>
        Tokens(await Send(HttpMethod.Post, "login", new { user = email, auth = password }));

    public Task SendOtp(string phone) =>
        Send(HttpMethod.Post, "phoneNumber/otp", new { mobile = phone, register = false });

    public async Task<(string Access, string? Refresh)> PhoneLogin(string phone, string otp) =>
        Tokens(await Send(HttpMethod.Post, "phoneLogin", new { mobile = phone, verificationToken = otp }));

    public Task ForgotPassword(string email) =>
        Send(HttpMethod.Post, "users/forgot-password", new { email });

    public async Task<(string Access, string? Refresh)> Refresh(string refreshToken)
    {
        var json = await Send(HttpMethod.Get, "users/refreshToken", headers: new() { ["refresh"] = $"JWT {refreshToken}" }) as JsonObject;
        var access = json.Str("accessToken");
        if (string.IsNullOrEmpty(access)) throw new ApiException(0, "Session expired. Please log in again.");
        return (access, json.Str("refreshToken"));
    }

    public async Task<UserProfile> Me()
    {
        var json = await Send(HttpMethod.Get, "myself", authorized: true) as JsonObject;
        // An expired or unknown token gets 200 {"user": null} rather than an error.
        if (json?["user"] is not JsonObject user) throw new ApiException(401, "Session expired. Please log in again.");
        return new UserProfile(user);
    }

    // VM

    public async Task<string> VmIp()
    {
        var ip = (await Send(HttpMethod.Get, "vm/getControllVmIp", authorized: true) as JsonObject).Str("vmip");
        if (string.IsNullOrEmpty(ip)) throw new ApiException(0, "VM IP not available yet.");
        return ip;
    }

    /// Hands the Moonlight pairing PIN to the backend, which enters it into Sunshine on the VM.
    public async Task<bool> VmAuth(string pin)
    {
        var json = await Send(HttpMethod.Post, "vm/vmAuthentication", new { code = pin }, authorized: true);
        if (json is JsonValue v && v.TryGetValue<bool>(out var ok)) return ok;
        if (json is JsonObject dict)
        {
            // Current server shape: {"message":"success","data":{"status":true|false}}
            if ((dict["data"] as JsonObject).Bool("status") is { } status) return status;
            foreach (var key in new[] { "success", "accepted", "result", "ok" })
                if (dict.Bool(key) is { } b) return b;
        }
        return false;
    }

    public async Task<bool> ConnectionStatus() =>
        (await Send(HttpMethod.Get, "vm/checkVmConnectionStatus", authorized: true) as JsonObject).Bool("connected") ?? false;

    /// Latest version of AntCloud's Windows client. A rise past `Config.MatchedClientVersion` has so far
    /// meant renamed API routes.
    public async Task<double?> WindowsClientVersion()
    {
        try
        {
            if (await Send(HttpMethod.Get, "windows/version") is not JsonObject json || json["version"] is not JsonValue v) return null;
            if (v.TryGetValue<double>(out var d)) return d;
            return v.TryGetValue<string>(out var s) && double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : null;
        }
        catch (Exception e) when (e is HttpRequestException or ApiException or TaskCanceledException) { return null; }
    }

    /// Server clock (IST), used for night-plan windows.
    public async Task<DateTimeOffset?> ServerTime()
    {
        try
        {
            var s = (await Send(HttpMethod.Get, "realtime") as JsonObject).Str("time");
            return DateTimeOffset.TryParse(s, out var d) ? d : null;
        }
        catch (Exception e) when (e is HttpRequestException or ApiException or TaskCanceledException) { return null; }
    }

    // Transport

    private async Task<JsonNode?> Send(HttpMethod method, string path, object? body = null,
                                       Dictionary<string, string>? headers = null, bool authorized = false,
                                       bool isRetry = false)
    {
        using var req = new HttpRequestMessage(method, Config.ApiBase + path);
        foreach (var (name, value) in headers ?? []) req.Headers.TryAddWithoutValidation(name, value);
        if (authorized && AccessToken != null) req.Headers.TryAddWithoutValidation("Authorization", $"JWT {AccessToken}");
        if (body != null) req.Content = JsonContent.Create(body);

        using var res = await Http.SendAsync(req);
        var status = (int)res.StatusCode;
        var text = await res.Content.ReadAsStringAsync();
        JsonNode? json = null;
        try { json = JsonNode.Parse(text); } catch (System.Text.Json.JsonException) { }

        // Poll endpoints are logged only when something is off; auth bodies are never logged.
        // checkVmConnectionStatus answers 400 {"connected":false} until Moonlight connects — expected, so not logged.
        if (path != "vm/checkVmConnectionStatus")
        {
            var preview = AuthPaths.Any(path.StartsWith) ? "" : " " + text[..Math.Min(text.Length, 300)];
            Log.Info($"[api] {method} {path} → {status}{preview}");
        }

        if (status == 404 && text.Contains($"<pre>Cannot {method.Method} /api/"))
        {
            Log.Info($"[api] {method} {path} no longer exists on AntCloud's server");
            UpstreamStatus.Shared.MarkRouteMissing($"{method.Method} {path}");
            throw new ApiException(status, "AntCloud changed their service, so this no longer works. This app needs an update.");
        }

        if (status == 401 && authorized && !isRetry && RefreshHandler != null && await RefreshHandler() is { } fresh)
        {
            AccessToken = fresh;
            return await Send(method, path, body, headers, authorized: true, isRetry: true);
        }
        if (status is < 200 or >= 300) throw new ApiException(status, ErrorMessage(json) ?? $"Request failed ({status}).");
        return json;
    }

    private static (string, string?) Tokens(JsonNode? json)
    {
        var root = json as JsonObject;
        var user = root?["user"] as JsonObject;
        var access = root.Str("token") ?? user.Str("token") ?? throw new ApiException(0, "Login response did not include a token.");
        return (access, root.Str("refreshToken") ?? user.Str("refreshToken"));
    }

    private static string? ErrorMessage(JsonNode? json)
    {
        if (json is not JsonObject dict) return null;
        if (dict["errors"] is JsonArray errors)
        {
            var messages = errors.OfType<JsonObject>().Select(e => e.Str("message")).OfType<string>().ToList();
            if (messages.Count > 0) return string.Join("\n", messages);
        }
        // {"error":"Validation Error!","message":"Please Enter Valid Phone Number"}: the message is the useful part.
        return dict.Str("message") ?? dict.Str("error");
    }
}
