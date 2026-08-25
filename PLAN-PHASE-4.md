# Plan: ClaudeDeck phase 4 — answer permission prompts from the deck

<!-- 9 steps: 1 observe, 2 kill switch, 3 show, 4-5 decide, 6 queue, 7 danger, 8-9 remember. -->

## Goal

Answer Claude Code's permission prompts from the Stream Deck, with console parity — **Allow**,
**Allow always**, **Deny** — **without taking the prompt away from the session**. The question
stays on screen exactly as it does today; the deck is a second way to answer it, for the
sessions you are not looking at.

## What changed since this plan was first written

The first version was built on `PreToolUse`, which can only decide by holding the call blind:
while it holds, the session shows a spinner and nothing else. That was rejected, and rightly —
a key that must be pressed before the question is even visible is a race the user loses.

`PermissionRequest` does not have that problem, and it is measured on this machine
([findings/holding-a-hook.md](docs/findings/holding-a-hook.md)):

- The session shows its own prompt **while the decision is still open**. A `deny` sent 15 s in
  dismissed the prompt and blocked the call; an `allow` sent 15 s in ran it.
- **Answering in the session cancels ours** — the client closes the hook's connection the moment
  the permission is resolved without us, so a slot key clears itself.
- **Waiting costs nothing.** If no answer comes, the hook times out and the prompt just stays.
- The event fires **only when Claude Code is about to ask**, so nothing has to predict it.
- The payload carries **`permission_suggestions`** — the dialog's own "always allow" options,
  returnable as `updatedPermissions`.
- The **`http` hook type** posts straight to the agent: no `curl`, no shim.

Three parts of the original plan are therefore gone: the permission predictor, its later
tightening against recorded sessions, and the wait budget the whole design had to be arranged
around.

## Assumptions / context

- [PLAN.md](PLAN.md) is complete: sessions, state, context fill, liveness and alerts already
  reach the deck.
- The return channel exists — hold-to-clear added the first hub-to-agent message and the
  addressing that finds the agent owning a session.
- Decisions are honoured in `default` mode (labelled **Manual**) and `dontAsk`. In `auto`,
  `acceptEdits` and the rest the hook fires and its decision is ignored, so the deck must say so
  rather than pretend.
- Measured on the **desktop application**, Claude Code 2.1.237. The terminal CLI is not
  installed on this machine and is untested.
- Every permission test is interactive and in a fresh session; hook reloading inside a running
  session is unreliable in both directions.
- Commit style: imperative subject with a subsystem prefix, plain language, essentials only.

## Risks

- **The agent holds an open request per waiting session.** Cheap, but it must survive several at
  once: a stand-in that handled one connection at a time serialised two sessions during the
  measurement, which is exactly the failure this phase must not ship. Covered by Step 1, whose
  verification uses two sessions.
- **A restarted agent drops held requests.** Safe by construction — the prompt stays on screen
  and the session waits for a human — but the deck must not keep showing a request it can no
  longer answer.
- **`allow` from a key is irreversible the moment the command runs.** Unchanged from the
  original plan: long press (Step 5), danger classification (Step 7), and the command shown
  before the key can be pressed (Step 3).
- **"Allow always" writes into the user's permission state.** `permission_suggestions` name
  their own destination, and `localSettings` means editing a file inside the user's repository
  from a key press. Step 8 keeps the decision ours: an agent-side store by default, so a
  mistaken press is revocable from the deck (Step 9).
- **Only the desktop application is measured.** Anything that turns out to be app-specific
  belongs in a finding, not in a workaround.

## Steps

### Step 1: See a session waiting for permission, and hold the question open

- **Change:** The repository's hooks gain `PermissionRequest`, delivered by the **`http` hook
  type** straight to the agent — the first hook that is not `curl`. The agent parks the request,
  marks the session `WaitingApproval`, and releases it with **no decision** on its own timeout or
  when the client aborts the connection because the session answered. Nothing decides yet. The
  session's `permission_mode` is recorded, because a mode that ignores decisions changes what
  the deck may claim later.
- **Files:** `.claude/settings.json`, `src/ClaudeDeck.Agent/*`, `src/ClaudeDeck.Core/Sessions/*`,
  `tests/ClaudeDeck.Agent.Tests/*`
- **Verify:** `dotnet test`; on the device, a session that stops for a `Bash` prompt turns amber
  and clears when the prompt is answered in the terminal; **two sessions waiting at once both
  show**, which is what proves the agent is not serialising them
- **Commit:** `agent: hold a permission request and show the session waiting`

### Step 2: Put the plugin's mode on a key

- **Change:** **Off / Observe / Active** as a key, defaulting to Observe. Off stops the deck
  flagging anything; Observe flags without deciding, which is what Step 1 built; Active is
  accepted and does nothing yet. Built before anything can decide, so the kill switch design
  §6.4 requires exists before the thing it switches off.
- **Files:** `src/ClaudeDeck.Plugin/Actions/*`, `src/ClaudeDeck.Core/Rendering/*`,
  `com.gyaltchik.claudedeck.sdPlugin/manifest.json`
- **Verify:** on the device, the key cycles the three modes and survives a plugin restart; in
  Off the amber state from Step 1 stops appearing
- **Commit:** `plugin: switch the plugin between off, observe and active`

### Step 3: Show what is being asked

- **Change:** The pending call on the deck: tool name and command text on the slot key, and the
  full command on the encoder's touch strip. Design §6.4-4 — no approval for something the user
  cannot read — is now shared with the session's own prompt, which shows the command in full
  anyway; the deck's job is to say *which* session is asking and *what* it is asking, so the
  right window can be found without hunting.
- **Files:** `src/ClaudeDeck.Core/Rendering/*`, `src/ClaudeDeck.Plugin/Actions/*`,
  `tests/ClaudeDeck.Core.Tests/*`
- **Verify:** on the device, a waiting session's key names the tool and shows as much of the
  command as fits, and the strip shows the command in full
- **Commit:** `plugin: show the pending permission request`

### Step 4: Deny from the deck

- **Change:** The first real decision, and deliberately the half that cannot wave anything
  through. In Active mode a Deny key answers the pending request with a canned reason, which the
  measurement confirmed reaches the model. Every failure path — no plugin, no hub, a mode that
  is not Active, a session in a mode the client ignores decisions in — leaves the prompt exactly
  where it is.
- **Files:** `src/ClaudeDeck.Protocol/*`, `src/ClaudeDeck.Hub/*`, `src/ClaudeDeck.Agent/*`,
  `src/ClaudeDeck.Plugin/Actions/*`
- **Verify:** `dotnet test`; interactively, a `Bash` prompt is answered from the deck and the
  session shows the denial with the reason; with the plugin stopped the same prompt waits for
  the terminal as usual
- **Commit:** `plugin: deny a pending permission request from the deck`

### Step 5: Allow, on a long press

- **Change:** The dangerous direction, added only once the request is readable. Held rather than
  tapped, for the reason hold-to-clear already established: a key is easy to brush against, and
  `allow` is irreversible the moment the command runs.
- **Files:** `src/ClaudeDeck.Plugin/Actions/*`, `src/ClaudeDeck.Agent/*`
- **Verify:** `dotnet test`; interactively, a command that would prompt runs when allowed from
  the deck; a short press does nothing
- **Commit:** `plugin: allow a pending permission request on a long press`

### Step 6: Several at once

- **Change:** More than one session can be waiting, and the deck must be unambiguous about which
  one a key press answers. The encoder scrolls the queue; the slot keys show who is in it.
- **Files:** `src/ClaudeDeck.Plugin/Actions/*`, `src/ClaudeDeck.Core/Rendering/*`
- **Verify:** on the device, two sessions waiting at once are both listed, the strip shows each
  one's command, and Deny answers the one on screen and no other
- **Commit:** `plugin: scroll between pending permission requests`

### Step 7: Classify dangerous commands and say so

- **Change:** `rm -rf`, `git push --force`, `curl | sh`, `sudo`, writes outside the working
  directory and paths that look like secrets. A dangerous call turns the key red and takes a
  deliberately longer hold. The classifier errs towards calling things dangerous: the cost is a
  longer press, and the cost of the opposite is the whole point of §6.4.
- **Files:** `src/ClaudeDeck.Core/Permissions/*`, `src/ClaudeDeck.Plugin/Actions/*`,
  `tests/ClaudeDeck.Core.Tests/*`
- **Verify:** `dotnet test` over a table of real command shapes; on the device a dangerous call
  is red and does not yield to the ordinary hold
- **Commit:** `core: mark dangerous requests and make them harder to allow`

### Step 8: Remember an "allow always"

- **Change:** The third console answer. The protocol offers `permission_suggestions` and would
  write the rule into the user's own settings; we keep the rule in **the agent's store** instead,
  matching design §6.2 — an accidental press must not edit a file inside the user's repository.
  A remembered rule then answers matching requests without the deck being touched.
- **Files:** `src/ClaudeDeck.Agent/*`, `src/ClaudeDeck.Core/Permissions/*`,
  `tests/ClaudeDeck.Agent.Tests/*`
- **Verify:** `dotnet test`; interactively, a command allowed always runs without a prompt on
  its second use, in a fresh session, with the deck untouched
- **Commit:** `agent: remember an allow-always decision`

### Step 9: List and revoke remembered rules from the deck

- **Change:** Without revocation, "allow always" is an irreversible decision taken by a key that
  is easy to press by accident — part of Step 8's safety, not a nicety. The encoder lists what
  has accumulated and a press takes one back.
- **Files:** `src/ClaudeDeck.Plugin/Actions/*`, `src/ClaudeDeck.Agent/*`
- **Verify:** on the device, a rule added in Step 8 is listed and can be removed, after which
  the same command prompts again
- **Commit:** `plugin: list and revoke remembered allow-always rules`

## Out of scope

- **Mirroring a remembered rule into `settings.local.json`.** Available through
  `updatedPermissions` with a `localSettings` destination, and design §6.2 keeps it as an
  explicit opt-in. An opt-in nobody has asked for is not worth building yet.
- **Moving the monitoring hooks off `curl` to the `http` type.** The `http` type works and would
  remove the shim from phase 5 as well, but converting the seven working hooks is separate from
  this phase and belongs with `claudedeck agent install`.
- **`Notification` with `permission_prompt`.** It arrives about six seconds after the prompt,
  which is worse than the event we now use for the same purpose.
- **Answering a prompt by typing into the terminal.** Still out of the question, and no longer
  needed by anything.
