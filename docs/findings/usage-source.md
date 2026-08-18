# Finding: the usage data source

**Resolved.** The gate decision for Phase 3 is green: real percentages and reset times are
available, so the estimate-only fallback (design §5.3) is not needed.

Found by reading [Sing3Rous/stream-deck-ai-limits](https://github.com/Sing3Rous/stream-deck-ai-limits),
an existing Stream Deck plugin that already shows Claude and Codex usage, and then confirmed
with a live request against this machine's credentials.

## The endpoint

```
GET https://api.anthropic.com/api/oauth/usage
Authorization: Bearer <access token>
anthropic-version: 2023-06-01
anthropic-beta: oauth-2025-04-20
```

Unofficial. It is what the Claude Code client itself calls, and it can change without notice.

## Credentials

`~/.claude/.credentials.json` holds `claudeAiOauth.accessToken`. That token is read and used
as found. Nothing else in the file is consulted.

### The token is never refreshed, and that is a safety decision

An earlier version refreshed an expired token against
`https://platform.claude.com/v1/oauth/token`, the way the client does. **That has been
removed.**

OAuth refresh rotates the refresh token server-side: the old one stops working and the caller
is expected to store the new one. We deliberately never write to the credentials file, because
it belongs to Claude Code — which means a refresh by us invalidates the token the client is
holding and gives it nothing back.

The symptom that prompted this: after a night of the plugin polling, the user's Claude Code
login in WSL had expired, and the credentials file had been rewritten with **empty** token
strings and `expiresAt` zeroed. Whether the plugin caused it was never proven, but the shape
of the risk is one-sided. A plugin that displays a percentage has no business being a
plausible reason for the tool it reports on to log its user out.

Consequences of not refreshing:

- If the stored token is expired, the request returns 401 and the key shows that a login is
  needed. Claude Code renews the token during normal use and rewrites the file; the next read
  picks it up.
- The stored `expiresAt` is not consulted at all. It was observed to be zero while the file
  still existed, which would have meant "always expired". The server is the only trustworthy
  judge of whether a token works.

### An unreadable file is not a login problem

`\wsl.localhost` stops serving when the distribution sleeps, and a process that held a
working path can keep failing on it after the share returns while a fresh process succeeds.
That is an outage, not a missing login, and the two must not share a message: one is fixed by
waiting, the other by the user acting. So a file that cannot be read reports as unavailable
and keeps showing the last good reading, while only a file that genuinely holds no token — or
a token the server rejects — asks for a login.

## Response shape

Confirmed with a live 200 response. Structure, with representative values:

```json
{
  "five_hour":  { "utilization": 24.0, "resets_at": "2026-01-01T13:59:59Z",
                  "limit_dollars": null, "used_dollars": null, "remaining_dollars": null },
  "seven_day":  { "utilization": 23.0, "resets_at": "2026-01-03T00:59:59Z" },
  "seven_day_opus": null,
  "seven_day_sonnet": null,
  "limits": [
    { "kind": "session",     "group": "session", "percent": 24, "severity": "normal",
      "resets_at": "...", "scope": null, "is_active": true  },
    { "kind": "weekly_all",  "group": "weekly",  "percent": 23, "severity": "normal",
      "resets_at": "...", "scope": null, "is_active": false }
  ],
  "spend": { "percent": 0, "severity": "normal", "enabled": false, "...": "..." },
  "extra_usage": { "is_enabled": false, "...": "..." },
  "member_dashboard_available": false
}
```

Notes:

- `utilization` and `percent` are 0..100, not fractions.
- `resets_at` is ISO-8601 with an offset.
- Several sibling windows exist and are usually `null`, including codenamed ones that appear
  to be internal experiments. Anything unrecognized must be ignored rather than displayed.

## Consume `limits[]`, not the named fields

The named windows (`five_hour`, `seven_day`, …) duplicate what `limits[]` already carries, but
`limits[]` adds two things worth having:

- **`severity`** — the server's own assessment (`normal` observed). This is a better source for
  key colour thresholds than numbers we invent, and it stays correct if the plan's shape changes.
- **`is_active`** — which limit is currently the binding one. That is exactly what a single
  summary key should show.

So: read `limits[]` first, fall back to the named fields when it is absent, and treat every
field as optional. This is an unofficial endpoint.

## Platform gap: no credentials file on Windows

`~/.claude/.credentials.json` exists inside WSL but **does not exist** in the Windows
`%USERPROFILE%\.claude` directory on this machine, which runs the desktop app. The Windows
token is stored somewhere else, most likely Credential Manager.

This is less of a problem than it looks. Usage belongs to an account, not a machine (design
§5.1), so a single agent holding valid credentials answers for the whole account, and the WSL
agent can do that. Locating the Windows token is worth doing for robustness, but it does not
block the feature.

## Polling

The reference plugin caches with a TTL and refreshes on demand. Design §5.2 already calls for
1–5 minute polling with backoff; nothing here changes that. The endpoint returns a 429 with
`retry-after` when hit too hard, which the backoff must honour.
