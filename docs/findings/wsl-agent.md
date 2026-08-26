# Finding: running the agent inside WSL2

**Works, and the code needed no change.** A Claude Code session inside a distribution reaches
the deck: the agent discovers the Windows host from its own default route, connects to the hub,
and its sessions appear beside the Windows ones.

One thing has to be arranged, and it is not obvious: **the two agents cannot share a port.**

Measured on 2026-08-26 with Ubuntu-24.04 (Ubuntu-20.04 also installed and running), the plugin
and the Windows agent on the same machine.

## What had to be done

- **The agent must be published self-contained.** There is no `dotnet` inside the distribution
  and none should be assumed:

  ```
  dotnet publish src/ClaudeDeck.Agent -c Release -r linux-x64 --self-contained true -o <out>
  ```

  The result is a single directory copied into the distribution. `curl` is already there.

- **The token comes from the environment**, `CLAUDEDECK_HUB_TOKEN`. The probe read it from the
  Windows profile through `/mnt/c`, which is fine for a probe and wrong for the shipped
  installer: a distribution cannot be assumed to have the Windows drive mounted.

- **The hooks go in the distribution's own `~/.claude/settings.json`**, the same nine events as
  the Windows repository's, posting to the agent's port on `127.0.0.1` inside the distribution.

Nothing else. `HubHost.Resolve` read `172.21.208.1` out of `/proc/net/route` and connected with
no configuration, exactly as designed in [wsl-transport.md](wsl-transport.md).

The hub logs the distribution in the agent's name — `RU-SO-WS-056/Ubuntu-24.04` — which is what
keeps two agents on one machine apart, since a distribution inherits the Windows host name.

## The port collision, and why it fails silently

**WSL2 publishes a port listening inside a distribution onto the Windows loopback.** That is
`localhostForwarding`, on by default, and it is implemented by a real Windows-side listener:

```
127.0.0.1:17800 <- wslrelay
```

So an agent inside WSL on the default port **takes that port away from the Windows agent**. What
follows is worse than a clash:

1. The Windows agent starts, prints `ClaudeDeck agent on http://127.0.0.1:17800`, then throws
   `AddressInUseException` and dies. **The banner is printed before the bind**, so the log says
   it is listening on a port it never got.
2. Started detached, nothing surfaces the exception at all. `Start-Process` returns happily and
   the process is simply gone.
3. Every hook from every **Windows** session then reaches the **WSL** agent, because Windows
   `127.0.0.1:17800` now belongs to `wslrelay`. The WSL agent duly reports sessions with
   `cwd` of `D:\...` and a transcript path of `C:\Users\...` it cannot read at that path.
4. Both agents are connected to the hub, so the same session id arrives from two of them and
   the deck shows whichever reported last.

None of this announces itself. The symptom is a deck that looks right and a Windows agent that
is not running.

**The fix is separate ports, chosen deliberately rather than by luck of who started first.** The
probe put the WSL agent on 17810 via `CLAUDEDECK_AGENT_PORT`, and the hooks written into the
distribution point at the same. `claudedeck agent install` has to allocate this, not hope.

Two smaller consequences worth keeping:

- **The banner should follow the bind, not precede it**, and a failed bind should be reported
  rather than thrown into the void of a detached process.
- 17810 is now published back onto the Windows loopback by `wslrelay`. Harmless — nothing on
  Windows wants it — but it means the same trap waits for any second distribution. Each one
  needs its own port.

## Traps met on the way

- **The login shell in the distribution is `fish`.** Commands sent as `wsl.exe -- bash -lc "…"`
  were re-parsed and mangled. Write a script file and run `wsl.exe -d <distro> -- bash
  /mnt/d/…/script.sh`, which is the same rule the earlier reconnaissance arrived at.
- **A running Linux agent holds its own binary open**: copying a new build over it fails with
  `Text file busy`. The same trap the Windows agent sets, from the other side. Stop it first.
- **`~/.claude/settings.json` in a distribution is the user's own**, with their model and their
  marketplaces in it. Merge, never write. Back it up first, and never let a second run copy an
  already-patched file over the backup - which happened once here, and the original had to be
  restored by hand.

## Not measured

- **A Claude Code session in the second distribution.** Ubuntu-20.04 is running and reachable
  but has no agent and no hooks.
- **Whether the WSL agent can read a transcript.** Context fill for a WSL session was not
  checked; the paths are native Linux paths there, so nothing suggests a problem, but nothing
  measured it either.
- **Reboot behaviour.** The WSL subnet is assigned dynamically and the agent re-reads it at
  startup, but no reboot was performed here.
