# Plan: ClaudeDeck — reconnaissance, skeleton and session monitoring

<!-- 19 steps: 1-8 reconnaissance, 9-13 end-to-end skeleton, 14-19 session monitoring. -->

## Goal

Take the project from an empty directory to a working read-only monitor of Claude Code
sessions on a Stream Deck + XL: keys show each session's state and how full its context
window is, including sessions running inside WSL2. Along the way, settle every assumption
the later phases (usage and approve/deny) depend on.

## Assumptions / context

- Design: [docs/design.md](docs/design.md). This plan implements Phases 0 through 2 of it.
- Claude Code 2.1.232, installed both on Windows (`~/.claude`) and as a separate copy
  inside WSL Ubuntu-24.04. A second distribution, Ubuntu-20.04, is also running.
- .NET SDK 10.0.302. Tests with xUnit, key rendering with SkiaSharp.
- The repository is public from the first commit, which is where the sanitization
  requirements on reconnaissance artifacts and the early CI step come from.
- Commit messages: imperative subject with a subsystem prefix (`agent: ...`,
  `plugin: ...`), body in plain language, bullets over prose, essentials only.
- Reconnaissance hooks are registered in the **project's own** `.claude/settings.json`
  inside this repository, not in the global config, so experiments cannot disturb real
  sessions in other directories.
- A deliberate deviation from the design for Phases 0 through 2: the local hook → agent
  channel uses loopback HTTP rather than a unix socket or named pipe. `curl` exists on
  both Windows and WSL, and the hook contract (JSON on stdin, JSON on stdout) maps onto
  it directly. The NativeAOT shim from design §3.2 arrives in Phase 5, once there is a
  measurable load to justify it.

## Risks

- **Hook payloads may not match the assumptions in design §4.1.** → Resolved by Step 2.
- **`PreToolUse` may not give real control over permissions.** All of Phase 4 rests on
  this. → Resolved by Step 3.
- **The chosen Stream Deck library may not know about the new device's 6 encoders.** →
  Resolved by Step 4. If it fails, we move to Elgato's raw WebSocket protocol, roughly a
  day of work.
- **The WSL → Windows transport may not come up in the user's configuration.** → Resolved
  by Step 5.
- **The server-side `/usage` data source may be unavailable.** This is a gate decision for
  Phase 3, not for this plan. → Investigated by Step 6; on a negative result Phase 3
  becomes design §5.3 (absolute numbers, no percentages), and the user makes that call.
- **The `permissions` rule replica may be larger than it looks.** → Sized by Step 7. The
  risk is bounded by construction: both possible prediction errors are safe (design §6.3).
- **Detecting the session's PID by walking process ancestors may not work.** → Surfaces in
  Step 18; the inactivity-timeout fallback is built in the same step, so the step
  completes either way.
- **A public repository turns reconnaissance artifacts into a leak risk.** Steps 2, 6, 7
  and 14 handle real paths, configs, transcripts and an OAuth token. → Sanitization is
  part of the acceptance criteria for those steps, and `.gitignore` covers secrets from
  Step 1.

---

## Steps

### Step 1: Initialize the public repository — done

- **Change:** `git init`, a .NET `.gitignore` with a dedicated secrets block
  (`.credentials.json`, `*.token`, raw transcripts), LICENSE, a README, and the first
  commit carrying the design document. The repository is public from the first commit, so
  hygiene is set up now rather than retrofitted in Phase 6.
- **Files:** `.gitignore`, `LICENSE`, `README.md`, `docs/design.md`
- **Verify:** `git log --oneline` shows one commit; `git status` is clean; `.gitignore`
  covers `**/.credentials.json` and `**/*.jsonl` outside `tests/fixtures/`
- **Commit:** `repo: initialize public repository`

### Step 2: Capture real hook payloads

- **Change:** The project's `.claude/settings.json` registers every hook against a
  recorder script that appends the received JSON to `docs/findings/hooks/<event>.jsonl`
  and exits 0 without deciding anything. Run a session in this directory that touches
  every event. The result is recorded fact instead of assumption. Only **sanitized**
  samples are committed: paths, user names and command text replaced with placeholders.
- **Files:** `.claude/settings.json`, `tools/spike/capture-hook.ps1`,
  `tools/spike/sanitize.ps1`, `docs/findings/hooks/*.jsonl`
- **Verify:** records exist for `SessionStart`, `UserPromptSubmit`, `PreToolUse`,
  `PostToolUse`, `Notification`, `Stop` and `SessionEnd`, containing `session_id`,
  `transcript_path`, `cwd`, `tool_name` and `tool_input`. Separately, check whether the
  current permission mode arrives — design §6.3's accuracy depends on it. `Notification`
  has to be provoked deliberately, with a permission request and a minute of idling.
  Before committing: `git grep` for the user name and for real working paths under
  `docs/findings/` returns nothing.
- **Commit:** `spike: capture real hook payloads`

### Step 3: Confirm that `PreToolUse` actually decides

- **Change:** A stub hook returns `deny` for one harmless command, then `allow`, then
  `ask`. The observed behaviour goes into findings. This either confirms or demolishes the
  foundation of Phase 4.
- **Files:** `tools/spike/decide-hook.ps1`, `docs/findings/pretooluse.md`
- **Verify:** on `deny` the command does not run and the reason reaches the model; on
  `allow` no prompt is shown; on `ask` the prompt appears as usual
- **Commit:** `spike: confirm PreToolUse permission decisions take effect`

### Step 4: Bring up a minimal plugin on the real device

- **Change:** A plugin manifest and one action: a key with static text plus an encoder
  with text on the touch strip. The point is to check that the chosen library understands
  the Stream Deck + XL and all 6 encoders.
- **Files:** `com.gyaltchik.claudedeck.sdPlugin/manifest.json`, `src/ClaudeDeck.Plugin/*`,
  `docs/findings/streamdeck.md`
- **Verify:** the plugin appears in Stream Deck and both the key and the encoder render on
  the device; findings record whether all 6 encoders are addressable and whether the touch
  strip works
- **Commit:** `plugin: add minimal action to validate Stream Deck + XL support`

### Step 5: Verify the WSL → Windows transport

- **Change:** A script starts a listener on Windows and both distributions check whether
  they can reach it. Record the working mode (mirrored or NAT), the host address and any
  firewall settings required.
- **Files:** `tools/spike/check-wsl-transport.ps1`, `docs/findings/wsl-transport.md`
- **Verify:** a request from Ubuntu-24.04 and from Ubuntu-20.04 reaches the listener on
  Windows; findings record how that was achieved
- **Commit:** `spike: verify WSL2 to Windows host transport`

### Step 6: Investigate the data source behind `/usage`

- **Change:** Determine where the client gets its `/usage` numbers, by intercepting CLI
  traffic or analysing the bundle. Document the endpoint, headers and response shape, or
  record with evidence that the source could not be found.
- **Files:** `docs/findings/usage-source.md`
- **Verify:** findings contain either a reproducible way to obtain the same percentages
  and reset times `/usage` shows, or a "not found" conclusion describing what was tried.
  This is the gate decision for Phase 3. **Nothing reaches the public repository** — not
  the token, not fragments of it, not raw traffic dumps; only a description of the request
  and response shape.
- **Commit:** `spike: investigate usage data source`

### Step 7: Inventory the forms of `permissions` rules

- **Change:** Collect the real configs (enterprise → user → project → local) from Windows
  and both distributions, write down the **forms** of rules that occur, and size up the
  matcher replica for design §6.3. Generalized forms go into the repository, not the
  configs themselves.
- **Files:** `docs/findings/permissions.md`
- **Verify:** findings list the rule forms with sanitized examples and an estimate of the
  matcher's cost; no real paths or internal project names remain
- **Commit:** `spike: inventory permission rule forms`

### Step 8: Fold the findings back into the design

- **Change:** Update `docs/design.md` from steps 2 through 7: turn confirmed assumptions
  into statements, rewrite the refuted ones, and settle the Phase 3 decision. Later phases
  must build on what was verified.
- **Files:** `docs/design.md`
- **Verify:** no "to be verified in Phase 0" wording remains; §5 reflects the decision
  taken on the usage source
- **Commit:** `docs: fold reconnaissance findings into design`

### Step 9: Make the agent accept and record hook events

- **Change:** A `ClaudeDeck.Agent` project: a listener on `127.0.0.1`, `POST
  /hook/{event}`, events written to an NDJSON log, and an always-empty response that
  decides nothing. The project's `.claude/settings.json` switches from the spike scripts to
  `curl` against the agent. This is already a useful event recorder on its own.
- **Files:** `src/ClaudeDeck.Agent/*`, `.claude/settings.json`, `ClaudeDeck.sln`
- **Verify:** with the agent running, a session in this directory fills the log; **with the
  agent stopped, the session runs without delays or errors** — this checks the key property
  that the plugin never gets in the way
- **Commit:** `agent: record hook events over loopback HTTP`

### Step 10: Derive session state from events

- **Change:** The state machine from design §4.1, an in-memory session registry, and
  `GET /sessions` returning id, cwd, branch, model and state. Transitions covered by xUnit
  tests.
- **Files:** `src/ClaudeDeck.Agent/*`, `tests/ClaudeDeck.Agent.Tests/*`
- **Verify:** `dotnet test`; while a session works `GET /sessions` reports `Working`, and
  `Idle` once the model answers
- **Commit:** `agent: derive session state from hook events`

### Step 11: Set up CI for build and tests

- **Change:** GitHub Actions building the solution and running tests on every push and
  pull request, with a badge in the README. A public repository without a green build
  looks abandoned, and fixing accumulated CI in Phase 6 costs more than setting it up now,
  with the first tests in place.
- **Files:** `.github/workflows/build.yml`, `README.md`
- **Verify:** the workflow passes on GitHub after a push; a deliberately broken test fails
  the build
- **Commit:** `ci: build and test on push and pull request`

### Step 12: Connect the agent to the hub

- **Change:** `ClaudeDeck.Protocol` (a versioned message envelope), a WebSocket server in
  the plugin process, and a WS client in the agent: token handshake, heartbeat, reconnect
  with backoff.
- **Files:** `src/ClaudeDeck.Protocol/*`, `src/ClaudeDeck.Hub/*`, `src/ClaudeDeck.Agent/*`,
  `tests/ClaudeDeck.Hub.Tests/*`
- **Verify:** an integration test starts a hub and an agent and checks the handshake,
  rejection on a bad token, and reconnection after a drop
- **Commit:** `hub: accept agent connections over authenticated websocket`

### Step 13: Show a summary on a key

- **Change:** A `claudedeck.summary` action: "agents N / sessions M", rendered with
  SkiaSharp, with coalesced updates. The first end-to-end path from hook to agent to hub
  to hardware.
- **Files:** `src/ClaudeDeck.Plugin/*`, `com.gyaltchik.claudedeck.sdPlugin/manifest.json`
- **Verify:** on the device the numbers change when the agent starts and stops, and when a
  new session opens
- **Commit:** `plugin: show connected agents and session count on a key`

### Step 14: Compute context size from the transcript

- **Change:** `ClaudeDeck.Core` — incremental `.jsonl` reading by byte offset, extraction
  of the last `assistant` record, and the `input + cache_creation + cache_read` sum. Golden
  tests over a copy of a real transcript stripped of message text, paths and names, leaving
  only structural fields and `usage`.
- **Files:** `src/ClaudeDeck.Core/*`, `tests/ClaudeDeck.Core.Tests/*`, `tests/fixtures/*.jsonl`
- **Verify:** `dotnet test` — the fixture is expected to yield 83,005 tokens, a value taken
  from real data. Separately: the fixture contains no message content and no working paths,
  because it is published in an open repository
- **Commit:** `core: compute context size from transcript usage records`

### Step 15: Map model identifiers to context window sizes

- **Change:** A `model → window` table handling the `[1m]` suffix; an unknown model yields
  200k plus an "estimated" flag the UI can surface. Without this step, percentages for
  `opus[1m]` are five times too high.
- **Files:** `src/ClaudeDeck.Core/*`, `tests/ClaudeDeck.Core.Tests/*`
- **Verify:** `dotnet test`, covering `opus[1m]` → 1M, a plain model → 200k, and an unknown
  model → estimated flag
- **Commit:** `core: map model identifiers to context window sizes`

### Step 16: Report context fill level to the hub

- **Change:** The agent attaches a transcript reader to every registered session (debounced
  polling, no file watchers) and sends the fill percentage to the hub as deltas.
- **Files:** `src/ClaudeDeck.Agent/*`, `src/ClaudeDeck.Protocol/*`
- **Verify:** `GET /sessions` shows the percentage rising as a session works, and dropping
  after `PreCompact` fires
- **Commit:** `agent: track context fill percentage per session`

### Step 17: Render a session slot on a key

- **Change:** A `claudedeck.session` action: colour by state, ring by context fill, label
  showing project and branch. Slots are dynamic but **sticky**: a session takes the lowest
  free slot when it first appears and holds it until it ends. There is no reordering by
  activity — otherwise keys move under your fingers, and in Phase 4 the same key will be
  approving commands. A freed slot is reused only by a new session.
- **Files:** `src/ClaudeDeck.Plugin/*`, `com.gyaltchik.claudedeck.sdPlugin/*`
- **Verify:** on the device, two parallel sessions (one on Windows, one in WSL) occupy
  different slots and change colour and ring independently; while one of them works
  actively, neither slot moves; when a session ends its slot is freed and goes to the next
  new one
- **Commit:** `plugin: render session slot with state colour and context ring`

### Step 18: Detect dead sessions

- **Change:** Capture the session process PID by walking ancestors on `SessionStart`, plus
  transcript mtime tracking, with a transition to `Stale`. The inactivity-timeout fallback
  is built at the same time, so the step closes even if PID detection proves unreliable.
- **Files:** `src/ClaudeDeck.Agent/*`, `tests/ClaudeDeck.Agent.Tests/*`
- **Verify:** close a terminal without ending the session cleanly — the slot goes `Stale`
  within the configured timeout; tests cover the path where no PID was determined
- **Commit:** `agent: mark sessions stale when the process is gone`

### Step 19: Highlight sessions that need attention

- **Change:** A slot flashes on transition to `NeedsAttention` or `WaitingInput`, plus a
  global alert mute key. This closes Phase 2: the product becomes usable day to day.
- **Files:** `src/ClaudeDeck.Plugin/*`, `com.gyaltchik.claudedeck.sdPlugin/manifest.json`
- **Verify:** on the device, a session waiting for an answer starts flashing; mute stops
  the flashing without affecting the states themselves
- **Commit:** `plugin: alert on sessions needing attention`

---

## Later phases (to be detailed after Step 8)

Breaking these into atomic steps now would be premature — their shape depends on the
reconnaissance results.

- **Phase 3 — usage.** `IUsageProvider` over the source found in Step 6; keys and encoders
  for the 5-hour and weekly windows with percentage and reset time; polling with backoff;
  an honest "no data" when unavailable. The shape of this phase follows the gate decision
  in Step 6.
- **Phase 4 — approve/deny.** The `permissions` rule matcher (design §6.3), three answers
  with console parity (§6.2), the "Allow always" rule store with revocation, danger
  classification, long press, and the safety rules in §6.4. The matcher's size is refined
  by Step 7.
- **Phase 5 — WSL and remote.** Transports based on Step 5, the NativeAOT hook shim
  replacing `curl`, `claudedeck agent install` with automatic `settings.json` patching, and
  groundwork for the SSH tunnel.
- **Phase 6 — packaging.** Property Inspector, a ready-made Stream Deck + XL profile, a
  `.streamDeckPlugin` release build from CI, a README with screenshots, and agent
  installation instructions.

## Out of scope

- Approve/deny in any form. Until Phase 4 the plugin makes no decisions and does not
  interfere with sessions. Hooks in Phases 0 through 2 always hand control back without
  deciding, the isolated Step 3 aside.
- Displaying usage — that is Phase 3.
- Remote machines over SSH — groundwork in the protocol, implementation after v1.
- Sending prompts into a session, focusing a session's window, cost tracking — v2.
- macOS and Linux as the plugin host.
