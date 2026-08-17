# Finding: `PreToolUse` decisions take effect

**Confirmed.** The foundation of Phase 4 holds: a `PreToolUse` hook can both block a tool
call and pre-approve one that would otherwise prompt.

Tested interactively on Windows, Claude Code 2.1.232, with `tools/spike/decide-hook.ps1`,
which reacts only to two unique markers and passes every other call through untouched.

## `deny` — conclusive

Asked the session to run `echo claudedeck-probe-deny`.

The call was blocked **before running**, and the client displayed the hook's reason
verbatim: `claudedeck probe: forced deny`. The model saw the denial and reported it back
rather than silently continuing.

Both halves of what the design needs are present: the command does not execute, and the
reason reaches the model, so it can adapt instead of retrying blindly.

## `allow` — confirmed, with the prompt suppressed

Asked the same session to run `eval "echo claudedeck-probe-allow"`. It ran immediately,
with no permission prompt.

The counterfactual is what makes this meaningful. In an earlier session in the same
repository, without the decision hook, Claude Code **did** prompt for the identical command
shape — `eval "echo permission-check-ok"` — and waited for the user. With the hook returning
`allow`, the prompt never appeared.

So `allow` is evaluated ahead of the permission system and short-circuits it entirely.

## Why this is also the project's main hazard

This is exactly the capability Phase 4 needs, and exactly why design §6.4 is written as
non-negotiable. A key press will be able to wave through a command that Claude Code would
otherwise have stopped to ask about. The consequences are load-bearing:

- **Fail to `ask`, never to `allow`.** Every failure path — timeout, lost connection, dead
  plugin — must fall back to the normal prompt. Confirmed cheap to honour: the hook returning
  nothing at all leaves the session behaving exactly as if the hook did not exist, which is
  how the probe left every non-marker command alone.
- **Long press for dangerous commands.** A physical key is easy to hit by accident, and
  `allow` is irreversible once the command runs.
- **Show the full command before deciding.** The deck must never ask for approval of
  something the user cannot read in full.

## Operational notes

Two constraints, both learned by getting them wrong first:

- **The test must be interactive.** Under `claude -p`, `--allowedTools` did not stop `Bash`
  from running (see [hooks.md](hooks.md)), so a headless result would not have meant what it
  appeared to mean.
- **A hook added to an existing group is not picked up by a running session.** The first
  attempt produced no decision at all: the recorder in the same group kept firing while the
  newly added decision hook never ran. Installing or changing the agent's hooks requires a
  session restart, and the installer must say so.
