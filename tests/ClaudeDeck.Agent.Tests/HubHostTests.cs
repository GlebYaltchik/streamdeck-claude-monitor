using ClaudeDeck.Agent;

namespace ClaudeDeck.Agent.Tests;

public class HubHostTests
{
    /// <summary>
    /// Copied from the routing table of Ubuntu-24.04 on the measured machine. Under the
    /// default NAT mode the default gateway is the Windows host, which is where the hub is.
    /// </summary>
    private const string RouteTable =
        "Iface\tDestination\tGateway \tFlags\tRefCnt\tUse\tMetric\tMask\t\tMTU\tWindow\tIRTT\n" +
        "eth0\t00000000\t01D015AC\t0003\t0\t0\t0\t00000000\t0\t0\t0\n" +
        "eth0\t00D015AC\t00000000\t0001\t0\t0\t0\t00F0FFFF\t0\t0\t0\n";

    [Fact]
    public void The_default_gateway_is_read_from_the_routing_table()
    {
        Assert.Equal("172.21.208.1", HubHost.ParseDefaultGateway(RouteTable));
    }

    [Fact]
    public void A_table_without_a_default_route_yields_nothing()
    {
        var withoutDefault = string.Join('\n', RouteTable.Split('\n').Where(line => !line.Contains("01D015AC")));

        Assert.Null(HubHost.ParseDefaultGateway(withoutDefault));
    }

    [Fact]
    public void An_unreadable_table_yields_nothing()
    {
        Assert.Null(HubHost.ParseDefaultGateway("not a routing table"));
    }
}
