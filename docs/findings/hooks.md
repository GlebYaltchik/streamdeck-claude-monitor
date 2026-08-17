# Finding: real hook payloads

Captured from Claude Code 2.1.232 on two platforms:

- **WSL2 (Ubuntu-24.04)** — two headless `claude -p` runs in a scratch project.
- **Windows** — a live desktop session working in this repository.

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
| `SubagentStop` | yes (WSL) | |
| `Notification` | **no** | Needs an interactive session — headless never prompts |
| `PreCompact` | **no** | Needs a real compaction |

## Payload schema

Every event carries `session_id`, `transcript_path`, `cwd` and `hook_event_name`.
Additional fields per event:

| Event | Additional fields |
|---|---|
| `SessionStart` | `source` (`startup` observed) |
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

## Cost of the recorder

The PowerShell recorder takes about 550 ms per invocation, almost entirely process
startup, and it fires twice per tool call. The POSIX shell recorder is far cheaper. This
is direct evidence for the NativeAOT shim planned in design §3.2: at two invocations per
tool call, interpreter startup is the dominant cost and it is paid on every single tool
use.

## Still outstanding

`Notification` and `PreCompact` are not captured, and the Windows session produced only
tool events. Completing this needs one interactive Windows session started after the hook
configuration is in place, which triggers a permission prompt and then sits idle for over
a minute.
