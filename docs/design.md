# ClaudeDeck — a Stream Deck plugin for Claude Code

Design document. **Version 0.2** — reconnaissance complete, findings folded in.

Every claim here that says "confirmed" was measured against Claude Code 2.1.232 and
Stream Deck 7.5.0 on this machine. The measurements themselves live in
[findings/](findings/) and are the authority; this document is their consequence.

---

## 1. Goal and scope

A physical control surface for Claude Code: see the state of every session and what is
left of your usage limits without switching windows, and allow or deny what the agent
wants to do with a single key press.

**Target device:** Stream Deck + XL. Confirmed to report as `type: 13` with a 9x4 key grid
and a device pixel ratio of 2. The number of encoders is not reported by the protocol at
all — see §8.

**Platform:** Windows for the plugin, .NET 10. Agents are cross-platform.

**In scope for v1:** usage (5-hour and weekly windows), session monitoring (state and
context fill), approve/deny from the deck, and sessions running on Windows and inside WSL2.

**Out of scope for v1:** remote machines over SSH, sending prompts into a session, changing
the permission mode of a running session, macOS or Linux as the plugin host.

---

## 2. Where the data comes from

Claude Code has no public API for an outside observer. Three channels are used.

| Channel | What it gives | Status |
|---|---|---|
| **Hooks** (`.claude/settings.json`) | Lifecycle events in real time, and the ability to answer a permission request | Confirmed. Documented extension mechanism |
| **Transcripts** `~/.claude/projects/**/*.jsonl` | Tokens, model, cwd, branch, timings | Confirmed. Format stable but not a contract |
| **OAuth usage endpoint** | The same percentages and reset times `/usage` shows | Confirmed live. Unofficial, may change |

OTEL is not used: it reports spend, and what is needed is the remaining limit.

---

## 3. Architecture

A plugin on Windows cannot reliably watch `~/.claude` inside WSL — the 9p filesystem serves
reads but not change notification, and there is no return channel for approvals. Hence: **an
agent on every machine where Claude Code runs.**

```
                       ┌─────────────────────────────────────┐
   Stream Deck HW ─────│  Stream Deck App                    │
                       └──────────────┬──────────────────────┘
                                      │ WebSocket (Elgato protocol, spoken directly)
                       ┌──────────────▼──────────────────────┐
                       │  ClaudeDeck.Plugin  (win-x64)       │
                       │  ├─ Actions / SVG key composition   │
                       │  └─ Hub: WS server                  │
                       └──────▲───────────▲──────────▲───────┘
                              │           │          │
                    127.0.0.1 │  vEthernet│(WSL)     └── ssh -R (v2)
                              │           │                  │
                    ┌─────────┴──┐ ┌──────┴─────┐ ┌──────────┴────┐
                    │ Agent win  │ │ Agent WSL  │ │ Agent remote  │
                    └─────▲──────┘ └─────▲──────┘ └───────▲───────┘
                          │  local socket │                │
                    ┌─────┴──────────────┴────────────────┴──────┐
                    │  hook shim  (invoked by Claude Code)       │
                    └────────────────────────────────────────────┘
```

**Connections are made agent → hub**, which removes every NAT and firewall question.

**The hub lives inside the plugin process** for v1, written as a separate library so it can
move into a service later without a rewrite.

### 3.1 What the agent does

One process per machine:

1. Listens on a local socket for hook shims — accepts events, returns approval decisions.
2. Keeps a registry of live sessions and tracks their liveness.
3. Reads transcripts incrementally by byte offset with a debounced poll, no file watchers.
4. Holds a WebSocket connection to the hub; works standalone when the hub is away.
5. On one machine per account, polls the usage endpoint (§5).

### 3.2 The hook shim

Hooks fire on **every** tool call, so startup cost is paid constantly. Measured: a PowerShell
recorder cost about 550 ms per invocation, almost entirely interpreter startup, at two
invocations per tool call. A POSIX shell recorder was far cheaper.

- **Phase 5 target:** `claudedeck-hook`, a NativeAOT binary that forwards stdin to the agent
  socket and prints the response.
- **Interim and zero-install fallback:** `curl`. The hook contract — JSON in, JSON out — maps
  onto it directly.

The shim never blocks longer than its configured limit and exits 0 on any error.

---

## 4. Feature 1: session monitoring

### 4.1 State model

Confirmed against real payloads ([findings/hooks.md](findings/hooks.md)).

| Hook event | Transition |
|---|---|
| `SessionStart` (`source=startup`) | → `Idle`, register the session |
| `SessionStart` (`source=compact`) | **no transition** — same `session_id`, the session continues |
| `UserPromptSubmit` | → `Working` |
| `PreToolUse` | → `Working`, or → `WaitingApproval` when our own gate returns `ask` |
| `PostToolUse` | → `Working` |
| `Stop` | → `Idle` — the turn is over, this is "waiting for the user" |
| `PreCompact` | → `Compacting` |
| `SessionEnd` | → remove from the registry |

Key states: **Idle / Working / WaitingInput / WaitingApproval / Compacting / Stale / Error**.

Three things the payloads settled:

- **`Notification` is not used.** It does not fire for a permission prompt — measured, with
  the dialog on screen and no hook run. `WaitingApproval` is instead derived from our own
  `ask` decision in `PreToolUse`, which is a fact rather than a signal to hope for.
- **Model and git branch are not in any payload.** Both come from the transcript, which the
  agent reads anyway.
- **Subagents do not create sessions.** `SubagentStop` carries the parent `session_id` plus
  `agent_id`, `agent_type` and `agent_transcript_path`. A running subagent is an attribute of
  its parent, never a slot of its own. The subagent tool is named `Agent`, not `Task`.

**Liveness is mandatory, not defensive.** A session left open emits no `SessionEnd` — observed
directly, when a session could not be closed through the UI and simply never ended. Relying on
`SessionEnd` would pin a slot forever. So the agent also records the `claude` PID by walking
the hook process's ancestors, watches transcript mtime, and marks a session `Stale` when
neither shows life.

### 4.2 Context fill level

From the last `assistant` record in the transcript:

```
context_tokens = usage.input_tokens
               + usage.cache_creation_input_tokens
               + usage.cache_read_input_tokens
```

Verified against real data: `1 + 3928 + 79076 = 83,005`.

The denominator is the model's context window, from a `model → window` table that also
**parses the `[1m]` suffix**, which appears in real tool responses and names the window
outright.

**Correction to an earlier version of this section.** It said a model without the suffix is
a 200k model, and that reading `claude-opus-5[1m]` as 200k would overstate the percentage
fivefold. That is backwards for the current line-up, where a million tokens is the rule and
200k the exception. Measured here: a single request on a plain `claude-opus-5` — no suffix —
read **638,450** tokens of context, and one on `claude-opus-4-8` read 570,355. Assuming 200k
would have shown those keys three times past full.

An unknown model still falls back to 200k and is flagged as an estimate. Falling back low is
deliberate: understating the window overstates how full the context is, which warns early,
while the opposite would let a session reach its limit with the key still looking calm.

`PreCompact` marks the exact moment of compaction, so the real auto-compact threshold can be
calibrated from observation rather than guessed.

---

## 5. Feature 2: usage

**Goal: parity with `/usage`, nothing more.** Percentages for the 5-hour and weekly windows
and their reset times. No breakdowns by model, project or cost.

### 5.1 Usage belongs to an account, not a machine

Limits are counted per account, so cross-machine aggregation is unnecessary. One agent holding
valid credentials answers for the whole account; the others do not compute usage. The
aggregation key is the account identifier, so two accounts mean two independent budgets.

This removes an entire layer that an earlier draft carried: deduplicating token records across
machines, model weighting, and sliding windows computed from transcripts.

### 5.2 The source

Confirmed with a live 200 response. Full details in
[findings/usage-source.md](findings/usage-source.md).

```
GET https://api.anthropic.com/api/oauth/usage
Authorization: Bearer <token from ~/.claude/.credentials.json>
anthropic-version: 2023-06-01
anthropic-beta: oauth-2025-04-20
```

**Consume the `limits[]` array**, falling back to the named windows. `limits[]` adds
`severity` — the server's own assessment, a better source for key colours than thresholds we
invent — and `is_active`, which says outright which window is currently binding.

Rules that are not negotiable:

- The token is read only by the agent on its own machine. Never logged, never sent to the hub,
  never leaves that machine. Only finished percentages and reset times travel.
- **The credentials file is never rewritten.** Refreshed tokens live in memory. That file
  belongs to Claude Code, and racing it risks logging the user out of their own client.
- Poll every 1–5 minutes with backoff, honouring `retry-after` on 429. Never on the render loop.
- The endpoint is unofficial. It sits behind `IUsageProvider`, and when it fails the key shows
  "no data" instead of the plugin failing.

**Known gap:** on this machine `~/.claude/.credentials.json` exists inside WSL but not on
Windows, where the desktop app stores its token elsewhere. Because usage is per account, the
WSL agent covers it. Locating the Windows token is worth doing for robustness but blocks
nothing.

---

## 6. Feature 3: approve / deny from the deck

### 6.1 Mechanism

`PreToolUse` is the only hook that can decide, and **it was confirmed to work in both
directions** ([findings/pretooluse.md](findings/pretooluse.md)): `deny` blocked a command
before it ran and delivered the reason to the model; `allow` ran a command that had prompted
in an earlier session without the hook, with no prompt shown.

```json
{"hookSpecificOutput":{
  "hookEventName":"PreToolUse",
  "permissionDecision":"allow|deny|ask",
  "permissionDecisionReason":"approved on Stream Deck"}}
```

`tool_use_id` is supplied in the payload and links `PreToolUse` to `PostToolUse`, so it is the
correlation key for a pending approval. No identifier of our own is needed.

### 6.2 Console parity: three answers

The deck offers exactly what the terminal offers: **Allow**, **Allow always**, **Deny**.

**Deny** sends a canned reason, since a deck cannot type. Several keys with different canned
reasons are cheap and make a denial far more useful to the model.

**Allow always** has no equivalent in the hook protocol, so we implement it. It is stored in
**the agent's own rule store**, not written into the user's `.claude/settings.local.json`.
A physical key is easy to press by accident, and an accidental press that silently edits config
inside the user's repository is a bad default. Mirroring into `settings.local.json` stays
available as an explicit opt-in. Every accumulated rule is listable and revocable from the deck.

### 6.3 Predicting "would the console have asked?"

`PreToolUse` fires on every tool call, including long-allowed ones, so the agent has to work out
whether permission would have been required.

It evaluates the same inputs Claude Code does: the `permissions` chain (enterprise → user →
project → local), `defaultMode`, our own "Allow always" rules, and the session's live
`permission_mode` — **which is confirmed to arrive in the payload**, removing what would
otherwise have been the predictor's largest error source.

Two measurements shape the effort:

- **The configured rules are a small surface.** Across 24 real settings files, only two shapes
  occur: `Tool(argument)` and a bare tool name for MCP tools, almost all of it `allow` on
  `Bash`. See [findings/permissions.md](findings/permissions.md).
- **The real limit is Claude Code's built-in default policy** — the behaviour that lets `Read`
  through and stops for `Bash`. It lives in the client and cannot be read from any config, so it
  must be approximated. Also confirmed: `acceptEdits` does not cover `Bash`, so the mode alone
  never decides.

**Both prediction errors are safe, which is what makes the approximation acceptable:** a false
"would ask" costs one extra key press; a false "would not ask" returns `ask` and the console
prompts exactly as it does today. So it can start crude and tighten from observation, with no
stage of it unsafe.

**Global switch** (encoder): `Active`, `Observe` (highlight only, decide in the terminal), `Off`.

### 6.4 Safety rules (not negotiable)

The probe confirmed how much power this is: a key press can wave through a command Claude Code
would otherwise have stopped for.

1. **Fail to `ask`, never to `allow`.** Timeout, lost connection, dead plugin — all lead to the
   normal prompt. Confirmed cheap to honour: a hook that returns nothing leaves the session
   behaving exactly as if it were not installed.
2. **The shim's timeout is strictly shorter** than the hook timeout, so a decision always has
   time to be printed.
3. **Danger classification** over `tool_input` — `rm -rf`, `git push --force`, `curl | sh`,
   `sudo`, writes outside the cwd, secrets paths. Dangerous turns the key red and requires a
   long press. `keyDown` and `keyUp` arrive separately, so duration is measurable.
4. **The full command is visible before deciding**, on the encoder touch strip.

### 6.5 Limitations

- A prompt already on screen cannot be answered from outside. We decide at `PreToolUse` or not
  at all. Emulating keystrokes into a terminal is out of the question.
- Headless mode has a proper `--permission-prompt-tool`, which is the cleaner path if
  deck-launched sessions ever appear. Note that headless permission semantics differ from
  interactive ones, so anything permission-related must be tested interactively.

---

## 7. Transport

Measured in [findings/wsl-transport.md](findings/wsl-transport.md); simpler than assumed.

**Windows agent** — `127.0.0.1`.

**WSL2** — this machine runs default **NAT** mode, not mirrored, and it works anyway. Both
distributions reach the Windows host at the vEthernet gateway address, **with no firewall rule
and no user setup**. The hub binds to the vEthernet address rather than `0.0.0.0`, which keeps
it off every other interface.

**The address is assigned dynamically and must never be hardcoded.** The agent discovers the
host from its own default route; the hub discovers the adapter at startup and copes with it
being absent when no distribution is running. Practically: always bind `127.0.0.1`, additionally
bind the vEthernet address when present.

**File-drop fallback** — a shared directory under `/mnt/c`, already proven incidentally by every
probe in this reconnaissance. Retained for unusual networking.

**Remote machines (v2)** — `ssh -R`, reusing SSH authentication, nothing exposed.

**Authentication:** a token presented at handshake, mandatory regardless of bind address —
loopback included, since any local process can reach the port.

The hub owns the token and mints one on first run into `%LOCALAPPDATA%\ClaudeDeck\hub-token`.
An agent on the same machine reads that file. An agent inside WSL cannot see the Windows
profile, so it takes the value from `CLAUDEDECK_HUB_TOKEN` instead; handing it over is what
`claudedeck agent install` will do once it exists.

---

## 8. The device and the deck

Confirmed in [findings/streamdeck.md](findings/streamdeck.md).

**Talk to the Elgato WebSocket protocol directly.** An earlier draft planned to start on an SDK
wrapper and fall back to the raw protocol if it did not understand a device this new. The probe
reached a fully working plugin — registration, both controller types, SVG keys, encoder
feedback, key and dial input — in about 200 lines with no dependencies. The fallback is cheaper
than the thing it was hedging, and it cannot be broken by a wrapper that does not know
`type: 13`. `IDeckConnection` is kept regardless, because it makes rendering testable without
hardware.

**Keys are SVG data URLs**, confirmed rendering on the device. No imaging library, resolution
independence, and golden tests that compare strings instead of bitmaps.

**Encoders** deliver `dialRotate` with signed `ticks` and a `pressed` flag, use their own
coordinate space, and accept `setFeedback` against the `$B1` layout. The encoder count is not in
the protocol; only `type: 13` identifies the device.

**Coalescing is required.** One dial spin produced 116 events in seconds. Updates are capped at
about 4 Hz with a dirty flag per key.

### Recommended profile

| Encoder | Action | Rotate | Press |
|---|---|---|---|
| 1 | Usage, 5-hour | — | refresh now |
| 2 | Usage, weekly | — | refresh now |
| 3 | Session browser | scroll sessions | make active |
| 4 | Approval queue | scroll pending requests, full command on the strip | — |
| 5 | Plugin mode | Off → Observe → Active | — |
| 6 | Spare | | |

**Keys:** 6–8 session slots (colour by state, ring by context, label project and branch), an
approval block (**Allow / Allow always / Deny**, plus "leave it to the terminal"), an alert mute,
and a usage summary.

**Slots are dynamic but sticky.** A session takes the lowest free slot on first sight and holds
it until it ends; a freed slot goes only to a new session. No reordering by activity — keys that
move under your fingers are irritating in Phase 2 and dangerous in Phase 4, where the same key
approves commands.

---

## 9. Repository layout and stack

```
/src
  ClaudeDeck.Protocol/    # DTOs, protocol versioning
  ClaudeDeck.Core/        # transcript parsing, context maths, state machine, SVG composition
  ClaudeDeck.Agent/       # daemon: hook socket, session registry, usage provider, WS client
  ClaudeDeck.HookShim/    # NativeAOT, minimal
  ClaudeDeck.Hub/         # WS server and aggregation (library)
  ClaudeDeck.Plugin/      # Stream Deck plugin, actions, rendering
/tests
/tools/spike              # Phase 0 probes, kept for reference
/docs/findings            # the measurements this document rests on
com.gyaltchik.claudedeck.sdPlugin/
```

**Stack:** .NET 10, `System.Net.WebSockets`, xUnit. No imaging library, no Stream Deck SDK
wrapper. The only external surface is HTTP to one endpoint.

---

## 10. Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Hook payload format changes in a new Claude Code version | Medium | High | Contract tests against the captured samples, version in the handshake, degrade to read-only |
| A blocking hook wedges a session | Low | **Critical** | Hard timeout below the hook limit, fail to `ask`, kill switch |
| Accidental approval of a dangerous command | Medium | **Critical** | Danger classification, long press, full command shown first |
| The usage endpoint changes or disappears | High | Medium | Behind `IUsageProvider`; the key shows "no data" and the plugin survives |
| The built-in permission policy is approximated badly | High | **Low** | Both errors safe by construction (§6.3) |
| Session PID detection is unreliable | Medium | Low | Inactivity timeout fallback, built at the same time |
| Update floods saturate the deck socket | **Confirmed** | Medium | 4 Hz cap with per-key dirty flags, measured as necessary |

Resolved by reconnaissance and no longer tracked: the usage source being unavailable, WSL
networking needing user setup, and an SDK library not understanding the device.

---

## 11. Phases

**Phase 0 — reconnaissance. Complete.** Seven steps, seven findings documents, six assumptions
confirmed and five refuted. See [findings/](findings/).

**Phase 1 — skeleton.** Protocol, hub, agent, plugin; a key showing connected agents.

**Phase 2 — usage.** Moved ahead of session monitoring. It is now the cheapest phase — one HTTP
call behind an interface, a TTL cache, and two keys composing a string — and it produces a
visibly useful key immediately.

**Phase 3 — session monitoring (read-only).** Hooks to state, transcripts to context, session
slots, alerts. Useful on its own and completely safe.

**Phase 4 — approve/deny.** The rule predictor, three answers with console parity, the rule
store, danger classification, and §6.4 in full.

**Phase 5 — WSL and remote.** Transports, the NativeAOT shim, `claudedeck agent install`.
**The installer must state that hook changes require a session restart** — measured: a hook
added to an existing group is not picked up by a running session.

**Phase 6 — packaging.** Property Inspector, a device profile, a `.streamDeckPlugin` release
from CI, README with screenshots.

---

## 12. Feature suggestions beyond the original list

1. **Focus a session's window** — pressing a slot brings that terminal to the front. Medium
   risk, high value once five sessions are open.
2. **Managing "Allow always" rules** — without revocation, it is an irreversible decision.
3. **Canned denial reasons** — several Deny keys instead of one generic reason.
4. **Global alert mute** — one key, invaluable during calls.
5. **Sending canned prompts** — needs managed sessions or a `Stop`-hook hack. v2, but the
   protocol should leave room.

**Deliberately not doing:** emulating keyboard input into a terminal; changing a running
session's permissions from outside; any automatic `allow` when the deck is unreachable; usage
breakdowns beyond `/usage` parity.

---

## 13. Decisions

1. Approval mirrors the console: **Allow / Allow always / Deny**. No bespoke auto-approval modes.
2. "Allow always" lives in the agent's store; the user's config is not edited by default.
3. Usage means parity with `/usage`, from the server, per account.
4. Remote machines: groundwork now, implementation after v1.
5. The repository is public from the first commit, so everything entering it from a real session
   is sanitized, and tokens are never committed.
6. Session slots are dynamic but sticky.
7. Talk to the Stream Deck protocol directly; compose keys as SVG.

Nothing is open. Phase 1 can start.
