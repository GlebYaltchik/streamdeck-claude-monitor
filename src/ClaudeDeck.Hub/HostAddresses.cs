using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ClaudeDeck.Hub;

/// <summary>
/// Which addresses the hub listens on.
///
/// Loopback always, for the agent on this machine. In addition the WSL vEthernet address
/// whenever a distribution is running: under the default NAT mode that gateway is how WSL
/// reaches Windows. The subnet is assigned dynamically and changes across reboots, so it is
/// discovered every time and never configured. Binding the adapter rather than everything
/// keeps the port off every other interface.
/// </summary>
internal static class HostAddresses
{
    public static IReadOnlyList<IPAddress> Current() => [IPAddress.Loopback, .. Wsl(Adapters())];

    /// <summary>
    /// Split out from the adapter scan so the choice can be tested on a machine without WSL.
    /// </summary>
    public static IEnumerable<IPAddress> Wsl(IEnumerable<(string Name, IPAddress Address)> adapters) =>
        adapters
            .Where(adapter => adapter.Name.Contains("WSL", StringComparison.OrdinalIgnoreCase))
            .Where(adapter => adapter.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(adapter => adapter.Address);

    private static IEnumerable<(string Name, IPAddress Address)> Adapters()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses
                    .Select(unicast => (adapter.Name, unicast.Address)))
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }
}
