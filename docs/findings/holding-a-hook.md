# Finding: answering a permission from outside the session

**`PermissionRequest` is the mechanism phase 4 needs.** The session shows its own permission
prompt while the decision is still open, an answer from outside dismisses that prompt, and an
answer given in the session cancels the outside one. Both channels are live at once and
whichever answers first wins.

This replaces the design's assumption that `PreToolUse` would carry the feature. It does work —
measured below — but only by holding the call blind: while a `PreToolUse` hook holds, the
session shows nothing but a spinner, so a key press is the only way to answer, and a person who
is not watching the deck sees a session that merely looks slow.

Measured interactively on Claude Code **2.1.237**, desktop application on Windows, one fresh
session per configuration, with the spike in `.spike/hold-hook`: a stand-in agent reached over
the `http` hook type, answering only calls that carry a marker and passing everything else
through untouched. Timings are from the session transcript and the stand-in's log.

## `PermissionRequest`, measured

| Case | Decision sent | Prompt on screen | Call to result | Outcome |
|---|---|---|---|---|
| immediate deny | 0.0 s | never appeared | 0.09 s | blocked, `message` reached the model |
| deny while the prompt waits | 15.0 s | from the start | 15.09 s | prompt dismissed, call blocked |
| allow while the prompt waits | 15.0 s | from the start | 16.9 s | prompt dismissed, command ran |

**The session answering first cancels ours.** In an earlier run the user answered in the terminal
about five seconds in; the stand-in's write failed twenty seconds later with `connection
aborted`. The client closes the hook's connection the moment the permission is resolved without
it, which is exactly the signal the agent needs to clear a pending key.

**Nothing is lost by waiting.** The prompt is visible the whole time, so a hold costs the user
nothing: they can answer where they are. If our answer never comes, the hook times out and the
prompt simply stays. There is no race to lose and no fall-through to explain.

## The contract

```json
{"hookSpecificOutput": {
  "hookEventName": "PermissionRequest",
  "decision": {"behavior": "allow"}}}

{"hookSpecificOutput": {
  "hookEventName": "PermissionRequest",
  "decision": {"behavior": "deny", "message": "denied on the Stream Deck"}}}
```

Four constraints, each of which cost a measurement round to learn:

- **`decision` is an object, not a string.** `{"decision": "deny"}` is rejected.
- **An unknown or invalid field fails schema validation for the whole response**, which the
  client treats as a non-blocking error: the prompt appears and waits. That is also the
  fail-safe — a malformed answer can never approve anything.
- **Decisions are honoured in `default` mode (labelled Manual), in `dontAsk`, and in
  `acceptEdits`.** The agent still reads `permission_mode` from the payload rather than
  assuming, but the list is wider than this finding first claimed - see the correction below.
- **`message` belongs to `deny` only.** Sending it beside `allow` is an unknown field.

## What the payload carries

`PermissionRequest` fires only when Claude Code is about to ask, which removes the predictor
design §6.3 called for: the client already knows the answer to "would the console have asked?"
and tells us.

It carries `tool_name`, `tool_input`, `permission_mode` — and **`permission_suggestions`, the
"always allow" options the dialog itself would offer**. A hook may return one of them as
`updatedPermissions`, which is equivalent to the user picking that option in the dialog. So the
third console answer, "Allow always", exists in the protocol and does not have to be invented.

There is no `tool_use_id` on this event, unlike `PreToolUse`.

## The transport

The **`http` hook type** posts the payload straight to a URL and reads the decision from the
response body — no `curl`, no shim. A 2xx with a JSON body is parsed exactly like a command
hook's stdout; a connection failure, a non-2xx or a timeout is a non-blocking error and the
prompt proceeds. The default timeout is 600 s.

`statusMessage`, which is documented to replace the spinner text while a hook runs, **never
appeared** in the desktop application.

## `PreToolUse`, measured first and kept as background

The earlier measurements stand and are what the fallback would rest on:

- A held call waits as long as the shim will wait, and a decision printed at the end still
  decides: 5 s, 15 s, 20 s and 45 s holds all behaved identically, and a 20 s `allow`
  suppressed the prompt entirely.
- **Exceeding the hook `timeout` falls through to the ordinary prompt.** The hook process is
  killed mid-sleep — the spike log has `hold-start` and no `hold-end` — and the call is neither
  failed nor run unasked.
- **The shim's timeout, not the hook's, sets the wait.** `curl --max-time 5` against a stand-in
  answering at 20 s exits 28 with empty stdout, and the prompt appears at once rather than at
  the hook's own 15 s timeout. A held answer delivered after 45 s through `curl --max-time 90`
  arrives intact; nothing listening costs 0.2 s.
- **A hold is invisible.** During a 45 s `PreToolUse` hold the screen shows the ordinary running
  state — spinner, elapsed counter, `Running tools...` — with nothing naming the hook and no
  offer to stop waiting.

## Correction: `acceptEdits` honours a decision (2026-08-26)

The bullet above once read "in `auto`, `acceptEdits` and the rest the hook still fires and its
decision is ignored". **That was a generalisation, not a measurement.** No row of the table
above names a mode; every case in it was run in `default`. `acceptEdits` was swept in with
`auto` on the assumption that a mode with "accept" in its name decides for itself.

It does not decide for itself. It accepts edits and stops for everything else, with the
ordinary three-way prompt - allow once, allow always, deny - in front of a person.

**Measured on Claude Code in WSL2 (Ubuntu-24.04), one fresh session in `acceptEdits`, with the
agent holding and the answer given on the Stream Deck:**

| Tool | Sent from the deck | Result |
|---|---|---|
| `Write` | `allow` | prompt dismissed, the write ran |
| `Bash` | `deny` | prompt dismissed, the call blocked |

Both were in the same session, one after the other, and both sides logged the round trip: the
plugin recorded `allow sent for Write` and `deny sent for Bash`, the agent
`answered on the deck: allow` and `answered on the deck: deny`.

`auto` is untested and stays out of the list. The claim about it has exactly the standing the
`acceptEdits` claim had until today, and should be measured before it is trusted.

### The reasoning that kept the list narrow was also wrong

The stated cost of holding a request in a mode that ignores decisions was that it "stalls the
session for the length of the hold and gains nothing". It cannot: **the event fires only when
the client is about to ask a person**, so a session whose request is held is already standing
still in front of somebody. The prompt is on screen the whole time and answering it there
closes ours. Holding costs nothing in any mode; the only thing the mode decides is whether our
answer counts.

### What the mode must never decide

Whether a session is **shown as waiting**. That was gated on the same list, and a session
stopped on an `mcp__youtrack__update_issue` prompt in `acceptEdits` was drawn on the deck as
working while its owner sat looking at the question - the deck claiming a session is busy while
a person waits is the exact failure it exists to prevent. Waiting is now marked in every mode,
and only the offer to answer is gated.

## Traps

- **The documentation was read through a summariser, and the summary was wrong.** Three
  measurement rounds failed on a decision schema that does not exist (`decision` as a string
  with an `allow|deny|escalate|noDecision` value). The page is served as markdown at
  `https://code.claude.com/docs/en/hooks.md`; read that, not a retelling of it.
- **Two hooks in one group confuse a measurement.** A second hook answering instantly beside a
  holding one made an early round unreadable. One hook, one variable.
- **The stand-in expired mid-session.** A whole round measured a dead port and looked like a
  client that ignores decisions. The stand-in now lives 24 h and is checked with a real request,
  not a socket connect, before anything is believed.
- **A single-threaded stand-in serialises sessions**, which is precisely the case phase 4 has to
  survive. It handles each connection on its own thread now.
