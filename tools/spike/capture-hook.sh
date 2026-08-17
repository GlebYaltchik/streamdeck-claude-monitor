#!/bin/sh
# Records a Claude Code hook payload for the reconnaissance phase.
#
# Reads the payload from stdin and appends it to $CLAUDEDECK_CAPTURE_DIR
# (default .spike/raw) as one JSON object per line. Writes nothing to stdout
# and always exits 0, so it can never influence a session or block a tool call.
#
# Output is raw and may contain real paths and command text, which is why
# .spike/ is gitignored. Sanitize before committing anything.
#
# Usage: capture-hook.sh <EventName>

event_name="$1"
output_dir="${CLAUDEDECK_CAPTURE_DIR:-.spike/raw}"

payload=$(cat)
if [ -z "$payload" ]; then
    payload="null"
fi

mkdir -p "$output_dir" 2>/dev/null

printf '{"capturedAt":"%s","hookArgument":"%s","projectDir":"%s","workingDir":"%s","payload":%s}\n' \
    "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
    "$event_name" \
    "$CLAUDE_PROJECT_DIR" \
    "$PWD" \
    "$payload" \
    >> "$output_dir/$event_name.jsonl" 2>/dev/null

exit 0
