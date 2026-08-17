namespace ClaudeDeck.Plugin;

/// <summary>
/// Append-only log next to the executable. Stream Deck gives a plugin no console, so this is
/// the only way to see what happened.
/// </summary>
internal static class PluginLog
{
    private static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "claudedeck.log");
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
                // Logging must never take the plugin down.
            }
        }
    }
}
