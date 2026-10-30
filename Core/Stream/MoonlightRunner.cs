using System.Diagnostics;
using System.Xml.Linq;
using AntCloud.Core.Api;
using Microsoft.Win32;

namespace AntCloud.Core.Stream;

/// Drives upstream Moonlight (moonlight-qt) through its command line
/// (`Moonlight pair <host> --pin N` and `Moonlight stream <host> <app> ...`).
///
/// The Windows build ships Moonlight's portable release in a `Moonlight` folder beside AntCloud.exe.
/// Portable Moonlight keeps its settings and pairings in an ini file in its own folder rather than the
/// registry, so AntCloud's pairing never touches a Moonlight the user installed themselves.
sealed class MoonlightRunner
{
    private Process? current;

    /// The bundled copy wins over an installed one.
    public static string? Locate()
    {
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "Moonlight", "Moonlight.exe"));
            foreach (var root in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
                candidates.Add(Path.Combine(Environment.GetFolderPath(root), "Moonlight Game Streaming", "Moonlight.exe"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            candidates.Add("/Applications/Moonlight.app/Contents/MacOS/Moonlight");
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications/Moonlight.app/Contents/MacOS/Moonlight"));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    public static bool IsInstalled => Locate() != null;

    /// True if Moonlight's saved hosts include `host` with a pinned server certificate. Running `pair`
    /// against an already-paired host pops an "already paired" dialog, so we skip it.
    public static bool IsPaired(string host)
    {
        try
        {
            var hosts = SavedHosts();
            return hosts.Any(h => h.Addresses.Contains(host) && h.HasCert);
        }
        catch (Exception e)
        {
            Log.Info($"[moonlight] couldn't read saved hosts: {e.Message}");
            return false;
        }
    }

    private sealed record SavedHost(HashSet<string> Addresses, bool HasCert);

    private static readonly string[] AddressKeys = ["manualaddress", "localaddress", "remoteaddress"];

    private static List<SavedHost> SavedHosts()
    {
        if (Locate() is { } exe && File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "portable.dat")))
            return IniHosts(Path.Combine(Path.GetDirectoryName(exe)!, "Moonlight Game Streaming Project", "Moonlight.ini"));
        if (OperatingSystem.IsWindows()) return RegistryHosts();
        if (OperatingSystem.IsMacOS()) return DefaultsHosts();
        return [];
    }

    /// Portable Moonlight: `[hosts]` with `1\manualaddress=…` and `1\srvcert=@ByteArray(…)` lines.
    private static List<SavedHost> IniHosts(string path)
    {
        if (!File.Exists(path)) return [];
        var hosts = new Dictionary<string, (HashSet<string> Addresses, bool Cert)>();
        var inHosts = false;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { inHosts = line == "[hosts]"; continue; }
            if (!inHosts) continue;
            var eq = line.IndexOf('=');
            var slash = line.IndexOf('\\');
            if (eq < 0 || slash < 0 || slash > eq) continue;
            var index = line[..slash];
            var key = line[(slash + 1)..eq];
            var value = line[(eq + 1)..].Trim('"');
            var entry = hosts.GetValueOrDefault(index, ([], false));
            if (AddressKeys.Contains(key) && value.Length > 0) entry.Addresses.Add(value);
            if (key == "srvcert") entry.Cert = value.Length > 0 && value != "@ByteArray()";
            hosts[index] = entry;
        }
        return hosts.Values.Select(h => new SavedHost(h.Addresses, h.Cert)).ToList();
    }

    /// Installed Moonlight on Windows: HKCU\Software\Moonlight Game Streaming Project\Moonlight\hosts\<n>.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static List<SavedHost> RegistryHosts()
    {
        using var root = Registry.CurrentUser.OpenSubKey(@"Software\Moonlight Game Streaming Project\Moonlight\hosts");
        if (root == null) return [];
        var hosts = new List<SavedHost>();
        foreach (var name in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(name);
            if (key == null) continue;
            var addresses = AddressKeys.Select(k => key.GetValue(k) as string).OfType<string>().Where(a => a.Length > 0).ToHashSet();
            var cert = key.GetValue("srvcert") switch
            {
                byte[] bytes => bytes.Length > 0,
                string s => s.Length > 0 && s != "@ByteArray()",
                _ => false,
            };
            hosts.Add(new SavedHost(addresses, cert));
        }
        return hosts;
    }

    /// Moonlight on macOS (development): `hosts.<n>.manualaddress` keys in its preferences.
    private static List<SavedHost> DefaultsHosts()
    {
        var info = new ProcessStartInfo("/usr/bin/defaults", ["export", "com.moonlight-stream.Moonlight", "-"]) { RedirectStandardOutput = true };
        using var process = Process.Start(info);
        if (process == null) return [];
        var xml = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        var dict = XDocument.Parse(xml).Root?.Element("dict");
        if (dict == null) return [];
        var values = new Dictionary<string, XElement>();
        var items = dict.Elements().ToList();
        for (var i = 0; i + 1 < items.Count; i += 2) values[items[i].Value] = items[i + 1];
        var count = int.TryParse(values.GetValueOrDefault("hosts.size")?.Value, out var n) ? n : 0;
        return Enumerable.Range(1, count).Select(i => new SavedHost(
            AddressKeys.Select(k => values.GetValueOrDefault($"hosts.{i}.{k}")?.Value).OfType<string>().Where(a => a.Length > 0).ToHashSet(),
            values.GetValueOrDefault($"hosts.{i}.srvcert")?.Value.Trim().Length > 0)).ToList();
    }

    /// Runs `pair` and waits for it to exit. Moonlight reports "already paired" as a failure, so callers
    /// should not treat a non-zero status as fatal — `stream` will fail clearly if unpaired.
    public Task<int> Pair(string host, string pin, CancellationToken ct) => Run(["pair", host, "--pin", pin], ct);

    /// Runs `stream` and waits until the Moonlight window closes.
    public Task<int> Stream(string[] args, CancellationToken ct) => Run(args, ct);

    public void Terminate()
    {
        try { if (current is { HasExited: false } p) p.Kill(entireProcessTree: true); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        current = null;
    }

    private async Task<int> Run(string[] args, CancellationToken ct)
    {
        var exe = Locate() ?? throw new ApiException(0, OperatingSystem.IsWindows()
            ? "Moonlight is missing from AntCloud's folder. Reinstall AntCloud, or install Moonlight from moonlight-stream.org."
            : "Moonlight was not found. Install it from moonlight-stream.org.");
        // Portable Moonlight looks for portable.dat in the current directory.
        var info = new ProcessStartInfo(exe, args) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false };
        var process = Process.Start(info) ?? throw new ApiException(0, "Moonlight didn't start.");
        current = process;
        try
        {
            await process.WaitForExitAsync(ct);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            Terminate();
            throw;
        }
    }
}
