namespace AntCloud.Core;

static class Config
{
    public const string ApiBase = "https://api.antcloud.co/api/";
    public static readonly Uri SocketUrl = new("https://socket.antcloud.co:8000");
    public const string Origin = "https://antcloud.co";
    public const string PricingUrl = "https://antcloud.co/pricing";
    public const string SignupUrl = "https://antcloud.co/signup";

    /// Value of the `connection` socket query param; the server knows AntCloud's own Windows client by it.
    public const string ConnectionType = "Windows";

    /// Sunshine app name started on the VM.
    public const string HostApp = "Desktop";

    /// UDP port on the VM that receives raw microphone PCM.
    public const int MicPort = 9002;

    // Version

    /// This app's version: `0.<AntCloud release>.<our release>`, the same scheme as the Mac app. 0.16.x matches
    /// AntCloud's Windows app 0.016, whose API routes `ApiClient` uses. Keep in step with <Version> in the csproj.
    public const string AppVersion = "0.16.3";

    /// The AntCloud Windows client version this build matches, read from `AppVersion` (0.16.x → 0.016).
    public static double MatchedClientVersion
    {
        get
        {
            var parts = AppVersion.Split('.');
            return parts.Length > 1 && double.TryParse(parts[1], out var minor) ? minor / 1000 : 0;
        }
    }

    // Credits

    public const string AppName = "AntCloud for Windows";
    public const string CreatorName = "Samyak Jain";
    public const string CreatorGitHub = "https://github.com/Sammy970";
    public const string CreatorX = "https://x.com/Sammy_970";
    public const string DiscordInvite = "https://discord.gg/GVDYtpWHps";
    public const string CreatorAvatar = "https://github.com/Sammy970.png?size=240";
    public const string MoonlightUrl = "https://moonlight-stream.org";
    public const string SourceUrl = "https://github.com/Sammy970/AntCloudWin";

    /// %APPDATA%\AntCloud on Windows (~/.config/AntCloud elsewhere): settings, the refresh token and logs.
    public static string DataDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AntCloud");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
