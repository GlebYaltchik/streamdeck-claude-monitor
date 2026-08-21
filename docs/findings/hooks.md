# Finding: real hook payloads

Captured from Claude Code 2.1.232 on two platforms:

- **WSL2 (Ubuntu-24.04)** — two headless `claude -p` runs in a scratch project.
- **Windows** — live desktop sessions working in this repository, including one started
  specifically to exercise a permission prompt and a manual `/compact`.

Sanitized samples live in [hooks/](hooks/), one `.jsonl` file per event, produced by
`tools/spike/sanitize-hooks.py`. Structure is preserved exactly; only free text, file
contents, command output and real paths are replaced.

## What was captured

| Event | Captured | Notes |
|---|---|---|
| `SessionStart` | yes (WSL) | |
| `UserPromptSubmit` | yes (WSL) | |
| `PreToolUse` | yes (both) | Write, Read, Edit, Bash, Agent |
| `PostToolUse` | yes (both) | |
| `Stop` | yes (WSL) | |
| `SessionEnd` | yes (WSL) | |
| `SubagentStop` | yes (both) | |
| `PreCompact` | yes (Windows) | `trigger=manual` |
| `Notification` | **never fired** | See below — it did not fire even for a real permission prompt |

## Payload schema

Every event carries `session_id`, `transcript_path`, `cwd` and `hook_event_name`.
Additional fields per event:

| Event | Additional fields |
|---|---|
| `SessionStart` | `source` (`startup` and `compact` observed) |
| `PreCompact` | `trigger` (`manual` observed) |
| `UserPromptSubmit` | `permission_mode`, `prompt`, `prompt_id` |
| `PreToolUse` | `permission_mode`, `prompt_id`, `tool_name`, `tool_input`, `tool_use_id`, `effort` |
| `PostToolUse` | the above plus `tool_response`, `duration_ms` |
| `Stop` | `permission_mode`, `prompt_id`, `last_assistant_message`, `stop_hook_active`, `background_tasks`, `session_crons`, `effort` |
| `SubagentStop` | the `Stop` fields plus `agent_id`, `agent_type`, `agent_transcript_path` |
| `SessionEnd` | `prompt_id`, `reason` (`other` observed) |

## Assumptions confirmed

- **`permission_mode` arrives in the payload.** This was the open question in design §6.3.
  It is present on `UserPromptSubmit`, `PreToolUse`, `PostToolUse`, `Stop` and
  `SubagentStop`, with `default` observed. The "would the console have asked?" predictor
  can therefore read the live mode instead of inferring it, which removes its largest
  source of error.
- **`tool_use_id` is supplied.** Design §6.1 planned to generate our own correlation id
  for approvals. Not needed — `tool_use_id` links `PreToolUse` to `PostToolUse` and is the
  natural key for a pending approval.
- **Hooks run with the working directory set to the project directory,** and
  `CLAUDE_PROJECT_DIR` is set to the same value. Relative paths in hook commands are safe.
- **The `[1m]` context suffix appears in real data.** The `Agent` tool response carries
  `resolvedModel: "claude-opus-5[1m]"`, confirming the concern in design §4.2 that the
  model→window table must parse that suffix.

## Assumptions refuted

- **`Notification` does not fire for a permission prompt.** This is the important one.
  Design §4.1 planned to detect "waiting for approval" from `Notification`. A Windows
  session was driven into a real permission prompt — Claude Code displayed the dialog and
  the user approved it — and no `Notification` hook ran. The hook is configured in the
  same shape as every hook that did fire, so the configuration is not the explanation.
  The 60-second idle trigger was not tested separately, but the design no longer depends
  on either.

  **What to use instead:** `PreToolUse` fires *before* the prompt is displayed. When our
  own gate returns `ask`, we know with certainty that a dialog is on screen right now.
  That is a fact derived from our own decision rather than a signal we have to hope for,
  and it is strictly more reliable than what was planned.
- **No `model` field in any hook payload.** Design §4.1 assumed `SessionStart` would
  supply the model. It does not. The model has to come from the transcript, which the
  agent reads anyway for context size.
- **No git branch in any hook payload.** Same conclusion: the transcript carries
  `gitBranch`, so the agent takes it from there.

## Discoveries that change the design

- **Subagents do not create separate sessions.** `SubagentStop` carries the *parent*
  `session_id` and identifies the subagent separately through `agent_id`, `agent_type` and
  `agent_transcript_path`. No `SessionStart` fires for a subagent. Session slots therefore
  cannot be polluted by subagents, and a running subagent is an attribute of its parent
  session rather than an entry of its own.
- **The subagent tool is named `Agent`, not `Task`.** Matters for any matcher configuration.
- **`tool_response` uses camelCase while `tool_input` uses snake_case** for the same
  fields — `newString` against `new_string`, `filePath` against `file_path`. A parser that
  assumes one convention will silently miss fields. This already caused a leak in the
  sanitizer, which is why it now normalizes key names before matching.
- **Hook configuration is picked up by an already-running session, but only partly.**
  Adding `.claude/settings.json` to this repository caused `PreToolUse` and `PostToolUse`
  to start firing in a session that was already open, while `SessionStart`,
  `UserPromptSubmit`, `Stop` and `SessionEnd` did not. Installing the agent will need a
  session restart to take full effect, and the installer should say so.
- **In headless mode `--allowedTools` did not prevent `Bash` from running.** Permission
  semantics under `claude -p` differ from interactive use. Step 3 must be verified
  interactively, not headlessly, or its result will not mean what it appears to mean.
- **Compaction reuses the session.** A manual `/compact` produced `PreCompact` followed by
  a second `SessionStart` with `source=compact` and **the same `session_id`**. The registry
  must treat that as the same session continuing, otherwise a compaction would move the
  session to a different key.
- **`acceptEdits` was observed as a live `permission_mode`,** and a `Bash` call still
  prompted while it was active. That is the predictor in design §6.3 working exactly as
  designed: the mode covers edits, not commands, so mode alone is not sufficient and the
  rule chain still has to be evaluated.
- **A session that is simply left open never emits `SessionEnd`.** One captured session has
  a `SessionStart` and no end, because the user had no obvious way to close it. This is
  direct evidence for the liveness problem in design §4.1: `SessionEnd` cannot be relied on
  to retire a slot, and the PID and mtime fallbacks are mandatory rather than defensive.
  The PID leg later turned out not to be available at all — see below.

## Liveness: what the agent can actually ask

Measured while building the stale-session sweep, on Claude Code 2.1.237 running inside the
Claude desktop app.

- **No hook payload carries a process id.** Not in the captured samples, not in any live
  event since. The schema above is the whole of it.
- **The interim shim cannot supply one either.** `curl` forwards stdin and exits; it knows
  nothing about its own ancestry and has nowhere to put it if it did. Design §4.1 described
  the agent recording the `claude` PID by walking the hook process's ancestors, which was
  written for the NativeAOT shim of §3.2 and does not survive the substitution.
- **Finding the process independently does not work here.** With sessions live in the
  desktop app, exactly one `claude-code` CLI process was running, parented to the app, and
  its command line named neither a session id nor a working directory. Even a process found
  by name cannot be matched to a session.
- **Transcript mtime does track live writes.** On a session mid-turn the file's last-write
  time trailed the wall clock by 2.6 seconds, so the directory entry is updated as the
  session appends rather than held until close.

So liveness rests on two witnesses, both already to hand: the last hook the session caused,
and the last write to its transcript. The PID leg waits for the shim.

## Cost of the recorder

The PowerShell recorder takes about 550 ms per invocation, almost entirely process
startup, and it fires twice per tool call. The POSIX shell recorder is far cheaper. This
is direct evidence for the NativeAOT shim planned in design §3.2: at two invocations per
tool call, interpreter startup is the dominant cost and it is paid on every single tool
use.

## Still outstanding

Only the 60-second idle variant of `Notification` is untested. It is not worth chasing:
the design no longer uses `Notification` for anything, since `PreToolUse` gives a more
reliable signal for the one state that mattered.

Nothing else blocks the next steps.
