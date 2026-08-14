# ClaudeDeck

A Stream Deck plugin for [Claude Code](https://claude.com/claude-code): see what every
session is doing, how full its context is, and how much of your usage window is left —
then approve or deny what the agent wants to do, from a physical key.

> **Status: early. There is no code yet — only a design and a plan.**
> Development starts with a reconnaissance phase that verifies every assumption
> against a real installation before anything is built on top of it.

## Planned capabilities

- **Usage** — 5-hour and weekly windows with reset times, matching what `/usage` shows.
- **Sessions** — one key per session: idle, working, waiting for input, or waiting for
  approval, with a ring showing how full the context window is.
- **Approve / Deny** — the same three options the terminal offers (Allow, Allow always,
  Deny), answered with a key press.
- **WSL2 and beyond** — sessions running inside WSL2 are first-class; the transport is
  designed so remote machines over SSH can follow later.

## How it works

Claude Code has no API for outside observers, so ClaudeDeck combines two supported
channels: **hooks** for session lifecycle and permission decisions, and **transcripts**
for context size. An agent process runs on each machine where Claude Code lives and
connects out to a hub hosted inside the plugin — which is what makes sessions inside
WSL2 reachable from a Windows plugin.

A safety property holds throughout: if the deck is unreachable, a hook times out, or the
plugin is not running, every permission request falls back to the normal terminal prompt.
**ClaudeDeck can never make a session less safe than it would be without it.**

## Target device

Developed against a Stream Deck + XL (36 keys, 6 encoders), but the actions are designed
to be layout-independent and should work on other Stream Deck models.

## Documentation

- [Design document](docs/design.md) — architecture, data sources, safety rules, risks.
- [Work plan](PLAN.md) — the atomic, commit-by-commit implementation plan.

Both are written in Russian.

## License

[MIT](LICENSE)
