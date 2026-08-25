# Plan: ClaudeDeck implementation

<!-- 15 steps: 1-5 usage on the deck, 6-9 the agent and hub, 10-15 session monitoring. -->

## Goal

Take the project from a validated set of assumptions to a Stream Deck that shows real usage
limits and the live state of every Claude Code session, including sessions inside WSL2.

## Reconnaissance is complete

Phase 0 ran as seven steps and produced [docs/findings/](docs/findings/). Everything below
rests on measurements rather than assumptions:

- [hooks.md](docs/findings/hooks.md) — real payloads for every event, and what they do not carry
- [pretooluse.md](docs/findings/pretooluse.md) — hook decisions confirmed in both directions
- [usage-source.md](docs/findings/usage-source.md) — the endpoint, confirmed live
- [rendering.md](docs/findings/rendering.md) — keys are SVG, no imaging library
- [streamdeck.md](docs/findings/streamdeck.md) — the device, the protocol, no SDK wrapper
- [wsl-transport.md](docs/findings/wsl-transport.md) — NAT works, no firewall rule
- [permissions.md](docs/findings/permissions.md) — the rule surface is small

[docs/design.md](docs/design.md) version 0.2 folds all of it in.

## Assumptions / context

- .NET 10, xUnit. No imaging library, no Stream Deck SDK wrapper — the plugin speaks the
  Elgato WebSocket protocol directly, as validated by the Phase 0 probe now living in
  `src/ClaudeDeck.Plugin`.
- Commit messages: imperative subject with a subsystem prefix, plain language, bullets,
  essentials only.
- **Usage needs no agent.** The plugin can read `.credentials.json` directly, including
  through `\\wsl.localhost\<distro>\home\<user>\.claude\` when the token only exists inside
  WSL. Reads over 9p are reliable; it is change notification and return channels that are not,
  and usage needs neither. The agent and hub arrive later, for sessions and approvals.
- Anything requiring a permission decision must be tested **interactively** and in a **fresh
  session**; both were learned the hard way in Phase 0.

## Risks

- **The usage endpoint is unofficial and can change with any release.** → Kept behind
  `IUsageProvider` from Step 2, with the key degrading to "no data" rather than the plugin
  failing. Accepted permanently; it is the price of the feature existing at all.
- **Hook payloads can change.** → Contract tests in Step 7 run against the captured samples in
  `docs/findings/hooks/`, so a format change fails a test instead of silently breaking a key.
- **Session PID detection may be unreliable.** → Surfaces in Step 14, where the inactivity
  fallback is built alongside it, so the step closes either way.
- **Update floods.** Measured: one dial spin produced 116 events. → Coalescing is built into
  the connection layer in Step 1, not retrofitted.

---

## Steps

### Step 1: Turn the probe into a plugin skeleton

- **Change:** Promote the Phase 0 probe into a real structure: a solution, the connection layer
  behind `IDeckConnection`, an SVG composition module, and update coalescing with a per-key
  dirty flag. One placeholder action, so the plugin stays loadable throughout.
- **Files:** `ClaudeDeck.sln`, `src/ClaudeDeck.Plugin/*`, `src/ClaudeDeck.Core/*`,
  `com.gyaltchik.claudedeck.sdPlugin/manifest.json`
- **Verify:** plugin loads in Stream Deck and the placeholder key renders; a burst of events
  produces at most ~4 updates per second per key
- **Commit:** `plugin: promote the probe into a plugin skeleton`

### Step 2: Fetch usage behind a provider

- **Change:** `IUsageProvider` with the real implementation: read credentials, refresh the token
  when expired, call the endpoint, retry once on 401, normalize `limits[]` into a snapshot with
  percentage, reset time and severity. Unit tests cover the response shapes, including missing
  fields and the codenamed windows that must be ignored.
- **Files:** `src/ClaudeDeck.Core/Usage/*`, `tests/ClaudeDeck.Core.Tests/*`
- **Verify:** `dotnet test`; a temporary console call returns the same numbers `/usage` shows
- **Commit:** `core: fetch and normalize usage behind a provider`

### Step 3: Set up CI for build and tests

- **Change:** GitHub Actions building and testing on push and pull request, badge in the README.
  Placed here because Step 2 produced the first tests, and a public repository without a green
  build looks abandoned.
- **Files:** `.github/workflows/build.yml`, `README.md`
- **Verify:** the workflow passes on GitHub; a deliberately broken test fails it
- **Commit:** `ci: build and test on push and pull request`

### Step 4: Show the 5-hour window on a key

- **Change:** A usage action: percentage, bar and reset time composed as SVG, colour driven by
  the server's `severity`. TTL cache with backoff, honouring `retry-after`. An explicit "no data"
  face when the endpoint or credentials are unavailable. Credentials path configurable in the
  Property Inspector, defaulting to the local file and accepting a WSL path.
- **Files:** `src/ClaudeDeck.Plugin/Actions/*`, `src/ClaudeDeck.Core/Usage/*`,
  `com.gyaltchik.claudedeck.sdPlugin/*`
- **Verify:** on the device, the key shows the same percentage as `/usage`; with credentials
  pointed at a nonexistent file it shows "no data" and the plugin keeps running
- **Commit:** `plugin: show the five-hour usage window on a key`

**This is the first genuinely useful build.**

### Step 5: Add the weekly window and the encoders

- **Change:** The weekly window as a second action, plus encoder variants using `setFeedback` on
  the `$B1` layout, with press to refresh now.
- **Files:** `src/ClaudeDeck.Plugin/Actions/*`, `com.gyaltchik.claudedeck.sdPlugin/manifest.json`
- **Verify:** on the device, both windows show on keys and on two encoders; pressing refreshes
- **Commit:** `plugin: add the weekly window and encoder variants`

### Step 6: Record hook events in the agent

- **Change:** `ClaudeDeck.Agent` listening on `127.0.0.1`, `POST /hook/{event}`, events to an
  NDJSON log, always an empty response. Hooks registered through `curl`. Useful on its own as an
  event recorder.
- **Files:** `src/ClaudeDeck.Agent/*`, `.claude/settings.json`
- **Verify:** a fresh session fills the log; **with the agent stopped the session runs without
  delay or error**, which is the property that must never regress
- **Commit:** `agent: record hook events over loopback HTTP`

### Step 7: Derive session state from events

- **Change:** The state machine from design §4.1, an in-memory registry, `GET /sessions`.
  Includes the three behaviours Phase 0 found: `SessionStart source=compact` continues a session
  rather than starting one, `SubagentStop` attaches to its parent, and `Notification` is unused.
  Contract tests replay the captured samples.
- **Files:** `src/ClaudeDeck.Agent/*`, `tests/ClaudeDeck.Agent.Tests/*`
- **Verify:** `dotnet test`, including a replay of `docs/findings/hooks/*.jsonl`; `GET /sessions`
  reports `Working` during a turn and `Idle` after it
- **Commit:** `agent: derive session state from hook events`

### Step 8: Connect the agent to the hub

- **Change:** `ClaudeDeck.Protocol` with a versioned envelope, a WebSocket server in the plugin,
  a client in the agent: token handshake, heartbeat, reconnect with backoff. The hub binds
  `127.0.0.1` and additionally the WSL vEthernet address when present, discovered at runtime and
  never hardcoded.
- **Files:** `src/ClaudeDeck.Protocol/*`, `src/ClaudeDeck.Hub/*`, `src/ClaudeDeck.Agent/*`,
  `tests/ClaudeDeck.Hub.Tests/*`
- **Verify:** integration test covers handshake, rejection on a bad token and reconnection; an
  agent inside WSL connects to the Windows hub
- **Commit:** `hub: accept agent connections over authenticated websocket`

### Step 9: Show connected agents and sessions on a key

- **Change:** A summary action: agents and session count. Completes the path from hook to agent
  to hub to hardware.
- **Files:** `src/ClaudeDeck.Plugin/Actions/*`
- **Verify:** on the device the numbers change as agents start and stop and sessions open
- **Commit:** `plugin: show connected agents and session count`

### Step 10: Compute context size from the transcript

- **Change:** Incremental `.jsonl` reading by byte offset, last `assistant` record,
  `input + cache_creation + cache_read`. Golden tests on a sanitized fixture.
- **Files:** `src/ClaudeDeck.Core/Transcripts/*`, `tests/ClaudeDeck.Core.Tests/*`,
  `tests/fixtures/*.jsonl`
- **Verify:** `dotnet test` expects 83,005 tokens on the fixture; the fixture carries no message
  content or real paths
- **Commit:** `core: compute context size from transcript usage records`

### Step 11: Map models to context windows

- **Change:** `model → window` with the `[1m]` suffix parsed; unknown models fall back to 200k
  and are flagged as estimates. Without this, `opus[1m]` reads five times too full.
- **Files:** `src/ClaudeDeck.Core/*`, `tests/ClaudeDeck.Core.Tests/*`
- **Verify:** `dotnet test` covering `claude-opus-5[1m]` → 1M, a plain model → 200k, unknown →
  flagged
- **Commit:** `core: map model identifiers to context window sizes`

### Step 12: Report context fill to the hub

- **Change:** The agent attaches a transcript reader per session, debounced polling, sends the
  percentage as deltas. Model and branch come from the transcript, since no hook payload carries
  them.
- **Files:** `src/ClaudeDeck.Agent/*`, `src/ClaudeDeck.Protocol/*`
- **Verify:** `GET /sessions` shows the percentage rising during a turn and dropping after
  `PreCompact`
- **Commit:** `agent: track context fill percentage per session`

### Step 13: Render the session slot

- **Change:** A session action: colour by state, ring by context, label of project and branch.
  Slots are dynamic but sticky — lowest free slot on first sight, held until the session ends.
- **Files:** `src/ClaudeDeck.Plugin/Actions/*`, `src/ClaudeDeck.Core/Rendering/*`
- **Verify:** on the device, a Windows session and a WSL session occupy different slots and
  update independently; neither moves while the other works; a freed slot goes to the next new
  session
- **Commit:** `plugin: render session slot with state colour and context ring`

### Step 14: Detect dead sessions

- **Change:** PID captured by walking ancestors at `SessionStart`, transcript mtime, transition
  to `Stale`. The inactivity fallback is built at the same time. Not optional: a session left
  open never emits `SessionEnd`, which Phase 0 observed directly.
- **Files:** `src/ClaudeDeck.Agent/*`, `tests/ClaudeDeck.Agent.Tests/*`
- **Verify:** closing a terminal without ending the session cleanly moves the slot to `Stale`
  within the timeout; tests cover the path where no PID was found
- **Commit:** `agent: mark sessions stale when the process is gone`

### Step 15: Highlight sessions needing attention

- **Change:** Slots flash on `WaitingInput`, plus a global alert mute. Closes session monitoring.
- **Files:** `src/ClaudeDeck.Plugin/Actions/*`
- **Verify:** on the device a session waiting for an answer flashes; mute stops the flashing
  without changing the states
- **Commit:** `plugin: alert on sessions needing attention`

---

## Later phases

- **Approve/deny.** Broken into steps now that session monitoring is standing:
  [PLAN-PHASE-4.md](PLAN-PHASE-4.md).
- **WSL and remote.** The NativeAOT hook shim replacing `curl`, `claudedeck agent install`
  — which **must tell the user that hook changes require a session restart** — and the SSH
  tunnel for remote machines.
- **Packaging.** Property Inspector, a Stream Deck + XL profile, a `.streamDeckPlugin` release
  built from CI, README with screenshots.

## Out of scope

- Approve/deny in any form until its phase. Until then the plugin makes no decisions: hooks
  hand control back without deciding.
- Sending prompts into a session, focusing a session's window, cost tracking.
- macOS and Linux as the plugin host.
