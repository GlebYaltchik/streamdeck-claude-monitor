using System.Globalization;
using System.Net;

namespace ClaudeDeck.Agent;

/// <summary>
/// Where the hub is, seen from the agent.
///
/// On Windows the plugin is on the same machine, so loopback. Inside WSL2 under the default
/// NAT mode the Windows host is the default gateway. That subnet is assigned dynamically and
/// changes across reboots, so it is read at startup and never configured.
/// </summary>
internal static class HubHost
{
    public const string EnvironmentVariable = "CLAUDEDECK_HUB_HOST";

    private const string RouteTable = "/proc/net/route";

    public static string Resolve()
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } configured)
        {
            return configured;
        }

        return (OperatingSystem.IsLinux() ? ReadDefaultGateway() : null) ?? "127.0.0.1";
    }

    private static string? ReadDefaultGateway()
    {
        try
        {
            return ParseDefaultGateway(File.ReadAllText(RouteTable));
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Pulls the gateway out of the kernel routing table. The address there is the raw
    /// four bytes printed as little-endian hex, which is why it goes through
    /// <see cref="IPAddress"/> rather than being read as a number.
    /// </summary>
    public static string? ParseDefaultGateway(string routeTable)
    {
        foreach (var line in routeTable.Split('\n').Skip(1))
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (fields.Length < 3 || fields[1] != "00000000")
            {
                continue;
            }

            if (uint.TryParse(fields[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var gateway) &&
                gateway != 0)
            {
                return new IPAddress(gateway).ToString();
            }
        }

        return null;
    }
}
