# Finding: the WSL2 to Windows transport

**Works, and needs less setup than design §7 assumed.** No firewall rule, no `.wslconfig`
change, no mirrored networking.

Measured with a plain `TcpListener` on the Windows side and a `/dev/tcp` probe from both
installed distributions.

## The configuration actually in use

There is no `.wslconfig` on this machine, so WSL runs in its **default NAT mode**. Mirrored
networking — which design §7 named as the preferred path — is not in use and turned out not
to be needed.

The Windows host appears to WSL as the default gateway:

```
$ ip route show default
default via 172.21.208.1 dev eth0
```

On the Windows side that is the `vEthernet (WSL (Hyper-V firewall))` adapter.

## Results

| From | To | Result |
|---|---|---|
| Ubuntu-24.04 | gateway `172.21.208.1` | **reachable** |
| Ubuntu-20.04 | gateway `172.21.208.1` | **reachable** |
| either distro | `127.0.0.1` | refused, as expected under NAT |
| either distro | the `resolv.conf` nameserver | unreachable |

Both distributions behave identically.

**No firewall rule was required.** There are zero firewall rules mentioning the port, and
the connection still succeeded. The WSL adapter is managed by the Hyper-V firewall, which
permits this by default. Design §7 budgeted for a rule and user setup; neither is needed
here.

Do not probe the `resolv.conf` nameserver address. It is a DNS proxy, not the host, and it
answered nothing.

## Bind to the interface, not to everything

The first probe used `0.0.0.0`, which works but also publishes the port on every other
interface. Binding to the vEthernet address instead — `172.21.208.1` — stayed reachable from
both distributions while confining the listener to that interface. That is a property of the
bind itself rather than something the probe proved, but it is the correct default and costs
nothing.

The auth token from design §7 stays mandatory regardless of the bind address.

## The address is not stable

In NAT mode the WSL subnet is assigned dynamically and changes across reboots. **Nothing may
hardcode `172.21.208.1`.**

- The **agent** discovers the host at startup from its own default route. No configuration.
- The **hub** must bind to the vEthernet address, which means discovering the adapter at
  startup, and coping with it not existing yet when no distribution is running. Practical
  approach: always bind `127.0.0.1` for the local Windows agent, additionally bind the
  vEthernet address when the adapter is present, and re-check when it appears.

This dynamic behaviour is documented WSL NAT behaviour rather than something measured here —
no reboot was performed — but the cost of handling it is small and the cost of assuming a
fixed address is a plugin that breaks on the next restart.

## The file-drop fallback is already proven

Transport C in design §7 needs no separate test. Every WSL probe in this reconnaissance
read its script from `/mnt/d/...` and wrote captures back to the same Windows directory. The
shared filesystem works in both directions. It stays in the design as the fallback for users
whose networking is unusual, but on this machine the network path is the better one.
