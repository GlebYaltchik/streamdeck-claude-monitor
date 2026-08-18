using System.Net;

namespace ClaudeDeck.Hub.Tests;

public class HostAddressesTests
{
    [Fact]
    public void The_wsl_adapter_is_picked_out_by_name()
    {
        // The name Windows gives the adapter on the measured machine. The address itself is
        // assigned dynamically, which is the whole reason it is discovered rather than set.
        (string, IPAddress)[] adapters =
        [
            ("Ethernet", IPAddress.Parse("192.168.1.20")),
            ("vEthernet (WSL (Hyper-V firewall))", IPAddress.Parse("172.21.208.1")),
        ];

        Assert.Equal([IPAddress.Parse("172.21.208.1")], HostAddresses.Wsl(adapters));
    }

    [Fact]
    public void Adapters_without_wsl_in_the_name_are_left_alone()
    {
        (string, IPAddress)[] adapters =
        [
            ("Ethernet", IPAddress.Parse("192.168.1.20")),
            ("vEthernet (Default Switch)", IPAddress.Parse("172.17.0.1")),
        ];

        Assert.Empty(HostAddresses.Wsl(adapters));
    }

    [Fact]
    public void The_link_local_address_of_the_wsl_adapter_is_not_bound()
    {
        (string, IPAddress)[] adapters =
        [
            ("vEthernet (WSL (Hyper-V firewall))", IPAddress.Parse("fe80::1")),
            ("vEthernet (WSL (Hyper-V firewall))", IPAddress.Parse("172.21.208.1")),
        ];

        Assert.Equal([IPAddress.Parse("172.21.208.1")], HostAddresses.Wsl(adapters));
    }
}
