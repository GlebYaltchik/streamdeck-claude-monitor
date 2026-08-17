namespace ClaudeDeck.Plugin;

/// <summary>
/// The command line Stream Deck uses to launch a plugin:
/// <c>-port N -pluginUUID U -registerEvent E -info {json}</c>
/// </summary>
internal sealed record StreamDeckArguments(int Port, string PluginUuid, string RegisterEvent, string Info)
{
    public static StreamDeckArguments? Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            values[args[i].TrimStart('-')] = args[i + 1];
        }

        if (!values.TryGetValue("port", out var port) ||
            !values.TryGetValue("pluginUUID", out var uuid) ||
            !values.TryGetValue("registerEvent", out var registerEvent) ||
            !int.TryParse(port, out var portNumber))
        {
            return null;
        }

        values.TryGetValue("info", out var info);
        return new StreamDeckArguments(portNumber, uuid, registerEvent, info ?? "");
    }
}
