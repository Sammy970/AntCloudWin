namespace AntCloud.Core;

/// Appends timestamped lines to %APPDATA%\AntCloud\Logs\antcloud.log (and the debug output).
static class Log
{
    public static readonly string FilePath = Path.Combine(Config.DataDir, "Logs", "antcloud.log");
    private static readonly object Gate = new();

    static Log()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // Keep one previous run's log so a bug report has something to show, without growing forever.
        try
        {
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2_000_000)
                File.Move(FilePath, FilePath + ".1", overwrite: true);
        }
        catch (IOException) { }
    }

    public static void Info(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
        System.Diagnostics.Debug.WriteLine(line);
        lock (Gate)
        {
            try { File.AppendAllText(FilePath, line + Environment.NewLine); }
            catch (IOException) { }
        }
    }
}
