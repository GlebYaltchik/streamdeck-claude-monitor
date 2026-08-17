# Finding: the shape of real `permissions` rules

Scoping input for the predictor in design §6.3. Inventoried across the WSL home directory
and the Windows profile and source tree: 24 settings files, of which 3 define permissions.

Only rule *forms* are recorded here; no paths, project names or rule contents.

## Forms in use

| Bucket | Tool | Form | Count |
|---|---|---|---|
| allow | `Bash` | `Tool(argument)` | 35 |
| allow | `Read` | `Tool(argument)` | 2 |
| allow | `PowerShell` | `Tool(argument)` | 2 |
| allow | `mcp__<server>__<tool>` | bare | 3 |

That is the entire surface. Two shapes:

1. **`Tool(argument-pattern)`** — the overwhelming majority, and almost all of it `Bash`.
2. **A bare tool name** — used for MCP tools.

## What is conspicuously absent

Across every config on this machine:

- **No `deny` rules and no `ask` rules.** Only `allow` is used in practice.
- **No `defaultMode`.** The mode is switched at runtime instead, which matches the
  `acceptEdits` value observed live in the hook payloads.
- **No `additionalDirectories`, no `disableBypassPermissionsMode`.**
- **No enterprise managed settings.** The only machine-level file is `policy-limits.json`,
  which carries unrelated restrictions.

The matcher still has to support the other buckets to be correct, but the common path is
narrow: parse `allow`, match two shapes, and read the live `permission_mode` that the hook
payload already supplies.

## The real accuracy limit is elsewhere

This is the important conclusion, and it is not what the step was looking for.

Three files define rules; everything else falls through to Claude Code's **built-in default
policy** — the behaviour that silently permits `Read`, `Glob` and `Grep` while stopping to
ask about `Bash`, `Write` and `Edit`. That policy lives in the client, not in any config we
can read.

So replicating the configured rules is the easy half and it is nearly free. Approximating
the built-in defaults is the half that determines how often the deck asks about something
the console would have let through, or stays quiet about something it would have stopped
for.

Design §6.3 already covers this: both errors are safe by construction. A false "would ask"
costs one extra key press; a false "would not ask" returns `ask` and the console prompts
exactly as it does today. So the approximation can start crude — read-only tools pass,
everything else escalates — and be tightened from observed behaviour, without any stage of
it being unsafe.

## Caveat on the sample

This is one developer's machine. It shows what these configs look like in practice here, not
a distribution across users. Someone running a locked-down enterprise setup would exercise
`deny`, `ask` and managed settings, all of which the matcher must still handle correctly
even though nothing here uses them.
