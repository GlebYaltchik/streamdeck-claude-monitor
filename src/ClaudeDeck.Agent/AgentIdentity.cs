namespace ClaudeDeck.Agent;

/// <summary>
/// How this agent names itself to the hub.
///
/// The id is a stored random value rather than the machine name, because a WSL distribution
/// takes the Windows host name by default: the two agents on this machine would otherwise
/// claim to be the same one.
/// </summary>
internal static class AgentIdentity
{
    private const string DistroEnvironmentVariable = "WSL_DISTRO_NAME";

    public static string Id()
    {
        var path = Path.Combine(DataDirectory(), "agent-id");

        if (File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } stored)
        {
            return stored;
        }

        var id = Guid.NewGuid().ToString("n");
        Directory.CreateDirectory(DataDirectory());
        File.WriteAllText(path, id);
        return id;
    }

    /// <summary>What a key label shows: the host, and the distribution when inside WSL.</summary>
    public static string Machine() =>
        Environment.GetEnvironmentVariable(DistroEnvironmentVariable) is { Length: > 0 } distro
            ? $"{Environment.MachineName}/{distro}"
            : Environment.MachineName;

    public static string Platform() => OperatingSystem.IsWindows() ? "windows" : "linux";

    private static string DataDirectory() => Path.GetDirectoryName(EventLog.DefaultPath())!;
}
