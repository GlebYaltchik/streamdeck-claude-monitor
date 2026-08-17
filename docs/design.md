# ClaudeDeck — a Stream Deck plugin for Claude Code

Design document. Version 0.1 (draft for discussion).

> Reconnaissance is in progress. Where [docs/findings/](findings/) contradicts this
> document, the findings win — they are measurements, this is a plan. They are folded back
> in at Step 8 of [PLAN.md](../PLAN.md).

---

## 1. Goal and scope

A physical control surface for Claude Code: see the state of every session and what is
left of your usage limits without switching windows, and allow or deny what the agent
wants to do with a single key press.

**Target device:** Stream Deck + XL (9x4 keys, 6 encoders, touch strip).
The plugin must not depend on a specific layout — actions are designed to work on the
XL, the MK.2 and the Stream Deck + alike.

**Platform:** Windows for the plugin, .NET 10. Agents are cross-platform (Windows and
Linux).

**In scope for v1:**
- Usage: the 5-hour and weekly windows.
- Session monitoring: state and context fill level.
- Approve/Deny from the deck.
- Sessions running on Windows and inside WSL2.

**Out of scope for v1, but the architecture must not preclude it:** remote machines over
SSH, sending prompts into a session, changing the permission mode of a running session,
macOS/Linux as the plugin host.

---

## 2. The central problem: where the data comes from

Claude Code has no public API for an outside observer. There are four channels, and each
feature is assembled from a different combination of them.

| Channel | What it gives | Reliability |
|---|---|---|
| **Hooks** (`~/.claude/settings.json`) | Lifecycle events in real time, plus **the ability to answer** a permission request | High. A documented extension mechanism |
| **Transcripts** `~/.claude/projects/**/*.jsonl` | Full history: tokens, model, cwd, branch, timings | High. The format is stable, but it is not a contract |
| **OAuth token** `~/.claude/.credentials.json` | Server-side usage — the same numbers `/usage` shows | Low. Undocumented, breaks on updates |
| **OTEL** (`CLAUDE_CODE_ENABLE_TELEMETRY=1`) | Token and cost metrics | Medium. Reports spend, not remaining limit |

**Decision:** hooks for session state and approve/deny; transcripts for context fill
level; the server-side source behind the OAuth token for usage (see §5 — this is the
critical path for that feature); OTEL is not used, because it reports spend when what is
needed is the remaining limit.

---

## 3. Architecture

A plugin on Windows cannot reliably read `~/.claude` inside WSL. The 9p filesystem works
for reads, but inotify and FileSystemWatcher do not work across it — and, more
importantly, there is no return channel for approvals. Hence: **an agent on every machine
where Claude Code runs.**

```
                       ┌─────────────────────────────────────┐
   Stream Deck HW ─────│  Stream Deck App                    │
                       └──────────────┬──────────────────────┘
                                      │ WebSocket (Elgato SDK)
                       ┌──────────────▼──────────────────────┐
                       │  ClaudeDeck.Plugin  (win-x64)       │
                       │  ├─ Actions / key rendering (Skia)  │
                       │  └─ Hub: WS server 127.0.0.1:17801  │
                       └──────▲───────────▲──────────▲───────┘
                              │           │          │
                   loopback   │   WSL2 ──┘          └── ssh -R (v2)
                              │           │                  │
                    ┌─────────┴──┐ ┌──────┴─────┐ ┌──────────┴────┐
                    │ Agent win  │ │ Agent WSL  │ │ Agent remote  │
                    └─────▲──────┘ └─────▲──────┘ └───────▲───────┘
                          │ unix socket / named pipe      │
                    ┌─────┴──────────────┴────────────────┴──────┐
                    │  hook shim  (invoked by Claude Code)       │
                    └────────────────────────────────────────────┘
```

**Connections are made agent → hub.** This removes every NAT and firewall question: WSL
and remote machines dial out to Windows themselves. The hub listens on `127.0.0.1` only,
except in WSL NAT mode (see §7).

**The hub lives inside the plugin process.** That is simpler for v1, and agents reconnect
when Stream Deck restarts. It is written as a separate library so it can later move into
a service or tray app without a rewrite.

### 3.1 What the agent does

One process per machine, not per session:

1. Listens on a local socket for hook shims — accepts events, returns approval decisions.
2. Keeps a registry of live sessions and tracks their liveness.
3. Reads transcripts incrementally (byte offset plus a 500 ms poll, no file watchers —
   more reliable across platforms) and computes context size.
4. Holds a WebSocket connection to the hub, pushes deltas, receives decisions and config.
5. Keeps working when the hub is unreachable, simply accumulating state.

### 3.2 The hook shim

Hooks fire on **every** tool call, so startup time matters. Two options, both supported:

- **Primary:** `claudedeck-hook`, a NativeAOT binary (~2 MB, ~5 ms startup) that forwards
  stdin to the agent's local socket and prints the response to stdout.
- **Zero-install fallback:** `curl --unix-socket ... --data-binary @-`. The hook contract
  — JSON on stdin, JSON on stdout — maps onto curl directly.

The shim **never blocks longer** than its configured limit, and on any error exits
quietly with code 0. A session must not suffer because the deck went away.

---

## 4. Feature 1: session monitoring

### 4.1 State model

Derived from hooks. Below is the target state machine; the exact payload fields are
verified against the installed version in Phase 0.

| Hook event | Transition |
|---|---|
| `SessionStart` | → `Idle`, register the session (id, cwd, model, transcript_path, git branch) |
| `UserPromptSubmit` | → `Working` |
| `PreToolUse` | → `Working` (with the tool name on the key), or → `WaitingApproval` if the gate fired |
| `PostToolUse` | → `Working` |
| `Notification` | → `NeedsAttention` (Claude Code sends it on permission requests and after 60 s idle) |
| `Stop` | → `Idle` — **this is exactly "waiting for the user"**, the turn is over |
| `PreCompact` | → `Compacting` |
| `SessionEnd` | → remove from the registry |

The resulting key states: **Idle / Working / WaitingInput / WaitingApproval / Compacting /
Stale / Error**.

**Liveness.** `SessionEnd` does not arrive when a terminal is killed. So the agent also
(a) records the PID of the `claude` process on `SessionStart` by walking the hook
process's ancestors, and (b) watches the transcript's mtime. No process, or no events for
N minutes, means `Stale`. PID detection is a medium risk; the timeout fallback always
works.

### 4.2 Context fill level

Take the last `assistant` record from the tail of the transcript:

```
context_tokens = usage.input_tokens
               + usage.cache_creation_input_tokens
               + usage.cache_read_input_tokens
```

Verified against real data: `1 + 3928 + 79076 = 83,005`.

The denominator is the model's context window. A `model → window` table is required, and
it must handle the `[1m]` suffix — a configured model of `opus[1m]` means 1M rather than
200k. An unknown model falls back to 200k and is flagged as an estimate.

The key shows a percentage and a ring indicator, with a mark at the auto-compact
threshold. `PreCompact` gives the exact moment compaction happens, which can be used to
calibrate the real threshold.

---

## 5. Feature 2: usage (5-hour and weekly)

**The goal is parity with the client's `/usage` command, nothing more:** percentages for
the 5-hour and weekly windows, and their reset times. No breakdowns by model, project or
category.

### 5.1 Consequence: usage belongs to an account, not a machine

Limits are counted per account. Therefore:

- Cross-machine aggregation is **not needed** — one source per account is enough.
- Whichever agent holds valid credentials answers for the whole account; other agents do
  not compute usage.
- The aggregation key is the account identifier. If Windows and WSL are logged into
  different accounts, those are two independent budgets and two sets of keys.

This removes a whole layer from the project: deduplicating records across machines, model
weighting, and sliding windows computed from transcripts.

### 5.2 Primary provider: server-side data

`/usage` reports server-side truth. It cannot be reconstructed from transcripts in
principle — they contain neither the limit's denominator nor spend from other devices. So
the source has to be the same one the client uses.

`~/.claude/.credentials.json` holds `claudeAiOauth.accessToken`, `subscriptionType` and
`rateLimitTier`. The Phase 0 task is to determine where `/usage` gets its numbers — by
intercepting CLI traffic or analysing the bundle — and to implement `IUsageProvider` on
top of that source.

**This is the feature's critical path, not an optional improvement.** If the source turns
out to be unavailable, the feature as specified cannot be built — see §5.3.

Constraints accepted up front:
- The source is undocumented and may break when Claude Code updates. The provider sits
  behind an interface, degrades without taking the plugin down, and the key clearly
  indicates "no data".
- The token is read only by the agent on its own machine. It is never logged, never sent
  to the hub, and never leaves that machine. Only finished percentages and reset times
  travel to the hub.
- Polling is infrequent — once every 1 to 5 minutes, with backoff on errors. Never on the
  render loop.

### 5.3 Fallback if Phase 0 fails

If the server-side source is unavailable, degrade to an estimate from transcripts:
absolute token spend in the current 5-hour block and over the week, **without
percentages** — the denominator is unknown, and guessing it would be dishonest. The key
is explicitly marked as an estimate (`≈`).

That call is made on the Phase 0 findings, before Phase 3 begins.

---

## 6. Feature 3: approve / deny from the deck

### 6.1 Mechanism

`PreToolUse` is the only hook that can **decide**. Its stdout:

```json
{"hookSpecificOutput":{
  "hookEventName":"PreToolUse",
  "permissionDecision":"allow|deny|ask",
  "permissionDecisionReason":"approved on Stream Deck"}}
```

- `allow` — the tool runs, no prompt is shown.
- `deny` — the call is blocked and the reason goes back to the model.
- `ask` — the normal terminal prompt appears.

The flow: `PreToolUse` → shim → agent → hub → the key flashes → press → the decision
travels back → the shim prints JSON.

### 6.2 Console parity: three answers

The deck must offer exactly what the terminal offers: **Allow**, **Allow always**,
**Deny**.

**Allow** → `permissionDecision: "allow"`. Trivial.

**Deny** → `permissionDecision: "deny"` plus a `permissionDecisionReason`. One wrinkle: in
the console, "No" lets you *type* what to do differently. You cannot type on a deck, so a
canned reason is sent instead ("denied from Stream Deck"). Optionally, several keys with
different canned reasons ("leave that alone", "show me a plan first", "take another
approach").

**Allow always** has no equivalent in the hook protocol, so we implement it ourselves.
Two ways to store the rule:

| Option | Pros | Cons |
|---|---|---|
| **A. The agent's own store** (recommended) | Leaves the user's config alone; rules are easy to review and revoke from the deck; rollback is trivial | The rule does not apply when Claude Code runs without the plugin |
| B. Writing to `.claude/settings.local.json` | Exactly what Claude Code itself does; the rule outlives the plugin | A key press silently edits config inside the user's repository, and undoing it is manual |

**Decision: option A by default**, with optional mirroring into `settings.local.json`,
off by default. The reasoning: a physical key is easy to press by accident, and an
irreversible config edit triggered by an accidental press is a bad default.

Rule scope follows the console and depends on the tool: this session, this project, or a
specific command pattern. Every accumulated rule is visible and revocable through a
dedicated deck action.

### 6.3 Predicting "would the console have asked?"

`PreToolUse` fires on **every** tool call, including ones that were allowed long ago. For
the deck to receive exactly what the console would have shown, the agent has to work out
for itself whether permission would have been required.

To do that it reads and evaluates the same configuration Claude Code does:
- `permissions.allow` / `deny` / `ask` across the settings chain (enterprise → user →
  project → local);
- `defaultMode` (`default`, `acceptEdits`, `plan`, `bypassPermissions`);
- the session's current mode, which the user may have switched on the fly (Phase 0: check
  whether it arrives in the hook payload — if it does, accuracy improves sharply);
- our own "Allow always" rules from §6.2.

**Both possible prediction errors are safe, and that is the main argument for this
approach:**
- wrongly deciding it would ask → one extra request on the deck, the user presses Allow.
  The cost is a single key press;
- wrongly deciding it would not ask → we return `ask` and the console shows its normal
  prompt. Behaviour is exactly as it would be without the plugin.

Neither error can cause something the user did not permit to run automatically. So the
fidelity of the rule replica can be improved iteratively, starting with the simple cases.

**Global switch** (encoder): `Active` — requests go to the deck; `Observe` — only
highlight that a session is waiting, decisions happen in the terminal; `Off` — the hook
returns `ask` immediately and the plugin stays out of the way.

### 6.4 Safety rules (not negotiable)

1. **Fail to `ask`, never fail to `allow`.** A timeout, a dropped connection, a crashed
   plugin — all of them lead to the normal terminal prompt. The plugin physically cannot
   make a session less safe than it was without it.
2. **The shim's internal timeout is strictly shorter** than the hook timeout configured in
   Claude Code (say 20 s against a 30 s limit), so a decision always has time to be
   printed.
3. **Danger classification.** Heuristics over `tool_input`: `rm -rf`, `git push --force`,
   `curl | sh`, `sudo`, writes outside the cwd, access to `.env` or secrets. Anything
   dangerous turns the key red and requires a **long press** (800 ms). A physical key is
   far too easy to knock with an elbow.
4. **The full command text is visible before deciding** — on the encoder's touch strip,
   which fits more than a key does.

### 6.5 Limitations worth knowing up front

- A prompt **already displayed** in the terminal cannot be answered from outside. Either
  we decide at `PreToolUse` — our route — or not at all. Emulating keystrokes into the
  terminal is out of the question: fragile and unsafe.
- Headless and SDK mode has a proper `--permission-prompt-tool`. If "managed" sessions
  launched from the deck appear later, that is the cleaner path for them.

---

## 7. Transport to WSL2 and remote machines

Three transports behind one interface, chosen automatically with fallback.

**A. Loopback** (agent on Windows) — `ws://127.0.0.1:17801`. Trivial.

**B. WSL2 over the network.**
- *Mirrored networking* (`networkingMode=mirrored` in `.wslconfig`, Windows 11): WSL sees
  the host as `localhost`. Requires `hostAddressLoopback=true`. The cleanest path and the
  preferred option on Windows 11.
- *NAT (the older default)*: the host address is the default gateway from `ip route`. The
  hub has to listen on the vEthernet (WSL) interface and a firewall rule is needed. More
  setup for the user.

**C. WSL2 file-based fallback.** WSL writes into
`/mnt/c/Users/<user>/AppData/Local/ClaudeDeck/ipc/` and Windows polls the directory. Zero
network configuration, 50–100 ms latency, which is acceptable for approvals. Insurance
for users with a non-trivial network setup. Cheap to build and it substantially reduces
the "it just doesn't work for me" risk.

**D. Remote machines (v2).** `ssh -R 17801:127.0.0.1:17801 user@host` — the agent on the
remote machine connects to its own localhost and the tunnel leads back to the hub.
Nothing has to be exposed, and authentication reuses SSH. Optionally the plugin brings
the tunnel up and restarts it itself.

**Authentication:** a per-agent token, generated on the Windows side and presented during
the handshake. Connections without a token are rejected — mandatory, since in NAT mode
the port can be visible beyond loopback.

---

## 8. Layout on the Stream Deck + XL

Actions are designed to be layout-independent; the profile below is a recommendation.

**Encoders (6):**

| # | Action | Rotate | Press |
|---|---|---|---|
| 1 | Usage, 5-hour | — | refresh now |
| 2 | Usage, weekly | — | refresh now |
| 3 | Session browser | scroll sessions; the strip shows project, state and context | make active |
| 4 | Approval queue | scroll pending requests, full command text on the strip | — (decisions are keys only, so nothing is confirmed by accident) |
| 5 | Plugin mode | Off → Observe → Active | — |
| 6 | Spare (session count / "Allow always" rules) | | |

**Keys:** a row of 6–8 session slots (colour = state, ring = context, label = project and
branch; slot assignment is described in §13.6), an approval block (**Allow / Allow always
/ Deny** for console parity, plus "leave it to the terminal"), an alert mute key, and a
usage summary key.

Both usage keys show a percentage, a bar and the window's reset time — exactly what
`/usage` shows.

**Rendering:** SVG data URLs on a 144×144 canvas. Updates are coalesced to no more than
~4 Hz with a dirty flag per key, otherwise the Stream Deck WebSocket chokes.

---

## 9. Repository layout

```
/src
  ClaudeDeck.Protocol/    # DTOs, protocol versioning, shared by everything
  ClaudeDeck.Core/        # transcript parsing, context maths, state machine
  ClaudeDeck.Agent/       # daemon: hook socket, session registry, WS client
  ClaudeDeck.HookShim/    # NativeAOT, minimal
  ClaudeDeck.Hub/         # WS server and per-agent aggregation (library)
  ClaudeDeck.Plugin/      # Stream Deck plugin, actions, rendering
/tests
  ClaudeDeck.Core.Tests/  # golden tests over sanitized real .jsonl files
/tools
  install/                # agent installation and settings.json patching
/docs
  design.md  protocol.md  findings.md
com.gyaltchik.claudedeck.sdPlugin/
```

**Stack:** .NET 10, System.Net.WebSockets, xUnit. Keys are composed as SVG data URLs with
no imaging library — see [findings/rendering.md](findings/rendering.md).

**Stream Deck library:** start with BarRaider's StreamDeck-Tools, but **all contact with
the SDK is isolated behind `IDeckConnection`**. The device is new; if the library turns
out not to support 6 encoders, moving to Elgato's raw WebSocket protocol costs about a
day.

---

## 10. Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Hook payload format changes in a new Claude Code version | Medium | High | Contract tests, Claude Code version in the handshake, degrade to read-only monitoring |
| A blocking hook wedges a session | Low | **Critical** | Hard timeout below the hook limit, fail to `ask`, kill switch |
| Accidental approval of a dangerous command | Medium | **Critical** | Danger classification, long press, full command text before deciding |
| **Server-side usage source is unavailable** | Medium | **High** | Settled in Phase 0 before the feature is built; fallback is an estimate without percentages (§5.3) |
| Usage source breaks on a Claude Code update | High | Medium | Provider behind an interface, the key honestly shows "no data", the plugin does not crash |
| The `permissions` rule replica is inaccurate | High | **Low** | Both errors are safe by construction (§6.3): either one extra press, or the normal terminal prompt |
| WSL networking does not come up for the user | Medium | High | Three transports, including the file-based one |
| The Stream Deck library does not know the Stream Deck + XL | Medium | Medium | The `IDeckConnection` abstraction, verified in Phase 0 |
| Session PID detection is unreliable | Medium | Low | Fall back to an inactivity timeout |

---

## 11. Work plan by phase

**Phase 0 — reconnaissance (1–2 days, throwaway code).**
Five checks, each closing a risk from §10:
1. Capture real payloads for every hook on the installed version; confirm `PreToolUse`
   genuinely decides; check whether the current permission mode arrives in the payload.
2. **Find the data source behind `/usage`** — a gate decision for Phase 3; if it fails,
   the feature becomes §5.3.
3. Verify the manifest, all 6 encoders and the touch strip on a live Stream Deck + XL.
4. Verify the WSL transport (mirrored networking should be the first thing that works on
   Windows 11).
5. Size up the `permissions` rule replica against real configs.

The output is `docs/findings.md` and this document's assumptions either confirmed or
refuted.

**Phase 1 — skeleton.** Repository, solution, `Protocol`, a minimal hub, agent and
plugin. Success criterion: a key shows "agents connected: N" and the WSL agent is
visible.

**Phase 2 — session monitoring (read-only).** Hooks → state, transcripts → context,
session slot keys, alerts. **Already useful on its own** and completely safe.

**Phase 3 — usage.** `IUsageProvider` over the source found in Phase 0, keys and encoders
for the 5-hour and weekly windows with percentage and reset time, careful polling with
backoff, an honest "no data" when unavailable.

**Phase 4 — approve/deny.** The `permissions` rule replica (§6.3), three answers with
console parity (§6.2), the "Allow always" rule store, danger classification, long press,
and every safety rule in §6.4.

**Phase 5 — WSL and remote.** The three transports, `claudedeck agent install` with
automatic `settings.json` patching, the SSH tunnel.

**Phase 6 — packaging.** Property Inspector, a ready-made Stream Deck + XL profile, a
`.streamDeckPlugin` build, CI, README.

Phases 2 through 4 are independent once Phase 1 is done, so their order can change. The
recommended order is the one above: what cannot break anything comes first.

---

## 12. Feature suggestions beyond the original list

Ordered by value against cost:

1. **Focus a session's window** — pressing a slot brings that session's terminal to the
   front. Achievable through the PID for Windows sessions; harder for WSL, where it means
   matching on window title. Medium risk, high value: with five or more sessions, finding
   the right window is a task in itself.
2. **Danger classification for commands** — formally part of approval safety (§6.4), but
   valuable on its own as a visual signal.
3. **Managing "Allow always" rules** — a dedicated action listing accumulated rules with
   one-press revocation. Without it, "Allow always" becomes an irreversible decision.
4. **Canned denial reasons** — several Deny keys with different text instead of one
   generic reason. Cheap, and it makes a denial considerably more useful to the model.
5. **Global alert mute** — one key, invaluable during calls.
6. **Sending canned prompts** ("continue", "run tests", "commit") — needs either managed
   sessions or a hack through the `Stop` hook. Defer to v2, but account for it in the
   protocol.

**Deliberately not doing:** emulating keyboard input into the terminal; changing a running
session's `permissions` from outside; any automatic `allow` when the deck is unreachable;
usage breakdowns by model, project or cost — the goal is parity with `/usage`, exactly.

---

## 13. Decisions and open questions

**Decided:**
1. Approval on the deck mirrors the console: **Allow / Allow always / Deny**. No bespoke
   auto-approval modes.
2. "Allow always" is stored in the agent's own store; the user's config is not edited by
   default (§6.2).
3. Usage means parity with `/usage`, with no breakdowns. The source is server-side and
   belongs to an account, not a machine.
4. Remote machines: groundwork in the protocol and transport, implementation after v1.
5. The repository is public from the first commit. Consequences: a license and README
   immediately, CI early, and mandatory sanitization of everything that enters the
   repository from real sessions — hook payloads, configs, test fixtures. Tokens and raw
   traffic dumps are never committed.
6. Session slots are dynamic and not tied to a project, but **sticky**: a session takes
   the lowest free slot when it first appears and keeps it until it ends. Slots are never
   reordered by activity — keys that move under your fingers are irritating in Phase 2 and
   dangerous in Phase 4, where the same key approves commands.

**Open:**

Nothing outstanding. The one open question — what to do if the usage source could not be
found — was answered by [findings/usage-source.md](findings/usage-source.md): it was found
and confirmed, so §5.3 is dropped and the feature keeps its full shape.
