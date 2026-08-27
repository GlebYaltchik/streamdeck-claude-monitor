# Plan: ClaudeDeck phase 4 — answer permission prompts from the deck

<!-- 10 steps: 1 observe, 2 switch, 3 show, 4 answer, 5 settings, 6-7 pair, 8 danger, 9-10 remember. -->

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

**Answering moved onto the session key** after a standalone Deny key was built and tried. With
one session waiting it worked; with several it has to pick one, and "the oldest" is a guess the
key cannot explain in the space it has — a session name rarely fits on a key. The question and
its answer now live on the same key, and separate answer keys return in Steps 6 and 7, once a
press can say which session they mean.

## Assumptions / context

- [PLAN.md](PLAN.md) is complete: sessions, state, context fill, liveness and alerts already
  reach the deck.
- The return channel exists — hold-to-clear added the first hub-to-agent message and the
  addressing that finds the agent owning a session.
- Decisions are honoured in `default` mode (labelled **Manual**), in `dontAsk` and in
  `acceptEdits` — the last one measured on 2026-08-26, after this plan was written, and
  correcting a claim that had been generalised rather than measured. `auto` is untested and the
  deck does not offer to answer it. A session is shown as waiting in **every** mode; only the
  offer to answer is gated.
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
  original plan: a long press (Step 4), danger classification (Step 8), and the command shown
  before the key can be pressed (Step 3).
- **"Allow always" writes into the user's permission state.** `permission_suggestions` name
  their own destination, and `localSettings` means editing a file inside the user's repository
  from a key press. Step 9 keeps the decision ours: an agent-side store by default, so a
  mistaken press is revocable from the deck (Step 10).
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

- **Change:** **Observe / Active** as a key, defaulting to Observe. Observe flags without
  deciding, which is what Step 1 built; Active is accepted and does nothing yet. Built before
  anything can decide, so the kill switch design §6.4 requires exists before the thing it
  switches off.

  It was three modes at first. The third, Off, also stopped the agent holding a question open
  and stopped the deck flagging one — and no scenario wanted it: holding costs a session
  nothing, and the switch §6.4 asks for is Observe, which cannot act by construction. Two
  states also let the key and the settings checkbox say exactly the same thing.
- **Files:** `src/ClaudeDeck.Plugin/Actions/*`, `src/ClaudeDeck.Core/Rendering/*`,
  `com.gyaltchik.claudedeck.sdPlugin/manifest.json`
- **Verify:** on the device, the key switches between the two modes and survives a plugin
  restart
- **Commit:** `plugin: switch the plugin between off, observe and active`

### Step 3: Show what is being asked

- **Change:** The pending call on the deck: the tool and one line of the command on the slot
  key, and who is waiting on the encoder's touch strip. The strip was tried with the command
  itself and could not carry it — one dial's segment holds about two dozen characters, and at a
  size that fits them nothing is readable across a desk. Design §6.4-4 — no approval for
  something the user cannot read — is shared with the session's own prompt, which has the
  command in full and is on screen the whole time; the deck says which session is asking and
  roughly what about, so the right window is found without hunting.
- **Files:** `src/ClaudeDeck.Core/Rendering/*`, `src/ClaudeDeck.Plugin/Actions/*`,
  `tests/ClaudeDeck.Core.Tests/*`
- **Verify:** on the device, a waiting session's key names the tool and shows as much of the
  command as fits, and the strip names the session waiting
- **Commit:** `plugin: show the pending permission request`

### Step 4: Answer on the session's own key

- **Change:** A tap denies the question the key is showing, a hold allows it. Answering belongs
  on the key that shows the question: a separate answer key has to name which session it means,
  and a session name rarely fits on a key — with two sessions waiting, "deny" on its own is a
  guess. The gestures are borrowed while a question is open and given back when it closes;
  clearing a slot is unavailable meanwhile, because a session stopped at a question is not one
  anybody wants to drop. Allowing is held half again as long as clearing, so muscle memory from
  one cannot run a command through the other. A key that can answer differs from one that only
  reports by colour alone: movement already means the attention swell.
- **Files:** `src/ClaudeDeck.Plugin/Actions/SessionAction.cs`, `src/ClaudeDeck.Core/Rendering/*`,
  `src/ClaudeDeck.Protocol/*`, `src/ClaudeDeck.Hub/*`, `src/ClaudeDeck.Agent/*`
- **Verify:** `dotnet test`; interactively, a tap on a waiting slot denies with the reason
  visible to the model, a hold runs the command with no prompt, and neither works with the
  deck in observe
- **Commit:** `plugin: answer a permission on the session key`

### Step 5: Turn answering off without a mode key on the deck

- **Change:** Today the only way to stop a key answering is an Approvals key on the deck. A
  panel without one has no switch at all, and the mode it inherits is invisible. The mode moves
  into the plugin's own settings, where an Approvals key sets it when there is one and a
  checkbox in the Property Inspector does when there is not. Answering off by default either
  way, and an unreadable setting reads as off rather than on.
- **Files:** `src/ClaudeDeck.Plugin/*`, `com.gyaltchik.claudedeck.sdPlugin/ui/*`
- **Verify:** on the device, a deck with no Approvals key answers nothing until the checkbox is
  ticked, and the choice survives a plugin restart; with an Approvals key present the two agree
- **Commit:** `plugin: keep the approval mode in the plugin settings`

### Step 6: Put a pair of answer keys on the deck

- **Change:** One new action, dropped on the deck twice: together the two keys are **Allow** and
  **Deny**. Which is which comes from their position, read the same way session slots are read,
  so a pair works with nothing configured. A checkbox in either key's settings swaps the pair —
  one setting, not two, so flipping one flips the other and the pair can never be two Allows.

  **Exactly two, or nothing works**, and every key says so on its own face rather than failing
  silently: one key on the deck, or three, and each of them says a pair is what it needs.

  Off — the Approvals mode on `observe` — the words go grey and the key reads as disabled. On,
  the role colours arrive dark and the key asks for nothing: the session keys are what swell
  for attention, and the pair must not compete with them. Brightness is kept back for the one
  moment it will mean something, which is Step 7.

  Nothing can be answered yet: a press does nothing, and the pair says nothing about tapping —
  there is nothing to tap yet. Built in this order for the same reason as Step 2: the pair has
  to be arrangeable, and has to explain itself, before it can act.
- **Files:** `src/ClaudeDeck.Plugin/Actions/AnswerAction.cs`,
  `src/ClaudeDeck.Core/Rendering/AnswerKeyFace.cs`, `src/ClaudeDeck.Plugin/PluginSettings.cs`,
  `com.gyaltchik.claudedeck.sdPlugin/manifest.json`, `com.gyaltchik.claudedeck.sdPlugin/ui/*`,
  `tests/ClaudeDeck.Core.Tests/*`
- **Verify:** `dotnet test`; on the device a single key says it needs a pair, a second key makes
  the two read Allow and Deny, a third makes all three say so again, the checkbox swaps both at
  once, and the pair reads as off while Approvals is on observe
- **Commit:** `plugin: add a pair of allow and deny keys`

### Step 7: Address one session, then answer it with the pair

- **Change:** What makes a standalone answer key honest: with a pair on the page and the mode
  `active`, a tap on a waiting session key makes that session the one the deck is talking about
  for **twenty seconds** and arms the pair, and Allow or Deny then answers it.

  **The session key stops answering altogether**, which is more than this step set out to do
  and is the only version that cannot be got wrong. A tap acknowledges and a hold clears,
  always, whatever state the session is in; a pair on the page adds a second meaning to the tap
  rather than replacing the first. Step 4 had the tap deny and the hold allow, and once the
  pair existed those meanings turned on whether it happened to be on the page being shown —
  which nobody pressing can see, because Stream Deck only tells a plugin about visible keys. A
  gesture decided by invisible state is one somebody is eventually wrong about, and being wrong
  about that one ran a command. Clearing a session stopped at a question is allowed for the
  same reason: it only takes the session off the deck, and the question stays answerable where
  it has been all along.

  The address is dropped when it is used, when the same key is tapped again, after twenty
  seconds, and **when the question it was for is gone** — answered in the session's own window,
  or replaced by another. That last one is not tidiness: an address that survives its own
  question is how the pair answers the *next* one.

  The pair gains the two faces Step 6 held back. A session waiting anywhere puts **tap a
  session** on both keys, still dark — the instruction appears when it is worth following. An
  address turns them **bright**, and in place of the instruction a bar drains right to left
  over the twenty seconds: short enough that keys going quiet unexplained would read as a
  fault. The addressed slot is framed, and the touch strip shows the addressed session rather
  than the oldest wait — the strip and the keys must never mean two different sessions.
- **Files:** `src/ClaudeDeck.Core/Permissions/Addressing.cs`,
  `src/ClaudeDeck.Plugin/Actions/SessionAction.cs`,
  `src/ClaudeDeck.Plugin/Actions/AnswerAction.cs`,
  `src/ClaudeDeck.Plugin/PendingQueue.cs`, `src/ClaudeDeck.Core/Rendering/*`,
  `tests/ClaudeDeck.Core.Tests/*`
- **Verify:** `dotnet test`; on the device, with two sessions waiting, addressing one and
  pressing Deny answers that one and no other; the bar drains and the pair goes idle after
  twenty seconds; answering in the session's own window drops the address rather than leaving it
  pointed at the next question
- **Commit:** `plugin: address a session and answer it from the pair`

### Step 8: Classify dangerous commands and say so

- **Change:** `rm -rf`, `git push --force`, `curl | sh`, `sudo`, writes outside the working
  directory and paths that look like secrets. The classifier errs towards calling things
  dangerous: the cost is a longer press, and the cost of the opposite is the whole point of
  §6.4.

  **The warning goes where the permission is given**, which is the Allow key — the pair is what
  answers now, and Deny gives nothing away, so it carries no warning and is never made harder.
  If a compact mode ever puts answering back on a session key, the warning goes there instead,
  by the same rule.

  It is the **background** that turns red rather than the word. Red already means Deny on this
  deck, and an Allow key coloured red would put two red words side by side saying neither. The
  background is where this deck has kept state since the session key was drawn; the word stays
  `allow`, stays green, and goes on saying which half of the pair it is.

  A dangerous allow is **held rather than pressed** — 1500 ms, longer than any hold a slot key
  has asked for. The gesture that changes is on the key that changed, in front of the person
  about to press it, which is the difference between this and the state-dependent gestures Step
  7 removed.
- **Files:** `src/ClaudeDeck.Core/Permissions/Danger.cs`,
  `src/ClaudeDeck.Core/Rendering/AnswerKeyFace.cs`, `src/ClaudeDeck.Plugin/Actions/AnswerAction.cs`,
  `tests/ClaudeDeck.Core.Tests/*`
- **Verify:** `dotnet test` over a table of real command shapes, including ordinary work that
  must **not** trip it; on the device a dangerous call turns the Allow key red and a press does
  nothing until it is held
- **Commit:** `core: mark dangerous requests and make them harder to allow`

### Step 9: Remember an "allow always"

- **Change:** The third console answer. The protocol offers `permission_suggestions` and would
  write the rule into the user's own settings; we keep the rule in **the agent's store**
  instead, matching design §6.2 — an accidental press must not edit a file inside the user's
  repository. A remembered rule then answers matching requests without the deck being touched.
- **Files:** `src/ClaudeDeck.Agent/*`, `src/ClaudeDeck.Core/Permissions/*`,
  `tests/ClaudeDeck.Agent.Tests/*`
- **Verify:** `dotnet test`; interactively, a command allowed always runs without a prompt on
  its second use, in a fresh session, with the deck untouched
- **Commit:** `agent: remember an allow-always decision`

### Step 10: List and revoke remembered rules from the deck

- **Change:** Without revocation, "allow always" is an irreversible decision taken by a key that
  is easy to press by accident — part of Step 9's safety, not a nicety. The encoder lists what
  has accumulated and a press takes one back.
- **Files:** `src/ClaudeDeck.Plugin/Actions/*`, `src/ClaudeDeck.Agent/*`
- **Verify:** on the device, a rule added in Step 9 is listed and can be removed, after which
  the same command prompts again
- **Commit:** `plugin: list and revoke remembered allow-always rules`

## Asked for, and deliberately not now

Both came from the device and are worth keeping; neither belongs in a step of this phase.

- **Merge the Alerts key and the Approvals key into one.** They ended up neighbours saying
  related things: one counts what is waiting and silences it, the other says how far the deck
  may go about it. One key could hold both, and should — but merging them changes a key that
  works, and doing it mid-phase would mean re-testing the switch on the hardware for a
  cosmetic gain. After the phase.
- **A surface for a deck with several encoders.** The touch strip cannot carry a command in one
  dial's segment, measured. A deck whose dials share one wide strip is a different surface with
  room for the whole thing, and is worth designing for on its own rather than as a stretched
  version of this one.

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
