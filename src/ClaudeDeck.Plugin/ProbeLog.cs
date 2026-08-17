namespace ClaudeDeck.Plugin;

/// <summary>
/// Append-only log next to the plugin. Stream Deck gives a plugin no console, so this file
/// is the only way to see what the device actually reported.
/// </summary>
internal static class ProbeLog
{
    private static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "probe.log");
    private static readonly Lock Gate = new();

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                File.AppendAllText(Path, line);
            }
            catch
            {
                // A probe must never take the plugin down over its own logging.
            }
        }
    }
}
