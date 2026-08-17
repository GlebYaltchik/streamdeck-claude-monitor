#!/usr/bin/env python3
"""Sanitizes raw hook captures so they can be committed to a public repository.

Reads .spike/raw/*.jsonl, replaces real paths and free-text content with
placeholders, and writes the result to docs/findings/hooks/.

Structure is preserved exactly: every key, every type, and every enum-like
value stays intact, because the point of the capture is to document the
payload schema. Only values that carry personal or project content are
replaced.
"""

import json
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
RAW_DIR = REPO_ROOT / ".spike" / "raw"
OUTPUT_DIR = REPO_ROOT / "docs" / "findings" / "hooks"

PATH_REPLACEMENTS = [
    (re.compile(r"/home/[^/\"\s]+"), "/home/<user>"),
    (re.compile(r"[A-Za-z]:[\\/]Users[\\/][^\\/\"\s]+"), "C:/Users/<user>"),
    (re.compile(r"/mnt/[a-z]/[^\"\s]*?/streamdeck-claude-monitor"), "<repo>"),
    (re.compile(r"[A-Za-z]:[\\/][^\"\s]*?[\\/]streamdeck-claude-monitor"), "<repo>"),
    (re.compile(r"/tmp/claudedeck-spike"), "<project>"),
    # Claude Code encodes the project path into the transcript directory name,
    # so it leaks the real location even after the paths above are scrubbed.
    (re.compile(r"projects[\\/][^\\/\"]+"), "projects/<encoded-project>"),
]

# Values under these keys are free text written by the user or the model, or
# file contents. They say nothing about the payload schema, so they go.
#
# Keys are matched after normalization, because Claude Code uses snake_case in
# tool_input and camelCase for the same field in tool_response.
REDACTED_KEYS = {
    "prompt",
    "last_assistant_message",
    "content",
    "command",
    "description",
    "old_string",
    "new_string",
    "original_file",
    "structured_patch",
    "stdout",
    "stderr",
}


def normalize_key(key):
    return key.replace("_", "").lower()


NORMALIZED_REDACTED_KEYS = {normalize_key(key) for key in REDACTED_KEYS}


def scrub_paths(text):
    for pattern, replacement in PATH_REPLACEMENTS:
        text = pattern.sub(replacement, text)
    return text


def sanitize(value, key=None):
    if key is not None and normalize_key(key) in NORMALIZED_REDACTED_KEYS:
        return f"<redacted {type(value).__name__}>"

    if isinstance(value, dict):
        return {k: sanitize(v, k) for k, v in value.items()}
    if isinstance(value, list):
        return [sanitize(item) for item in value]
    if isinstance(value, str):
        return scrub_paths(value)
    return value


PATH_LIKE = re.compile(r"[/\\]")


def collect_strings(value, found):
    if isinstance(value, dict):
        for item in value.values():
            collect_strings(item, found)
    elif isinstance(value, list):
        for item in value:
            collect_strings(item, found)
    elif isinstance(value, str) and PATH_LIKE.search(value):
        found.add(value)


def report_path_values(records):
    """Prints every path-like value left in the output for a human to eyeball.

    The redaction rules are a denylist, so they cannot prove the output is
    clean. Printing what survived is what makes review possible.
    """
    found = set()
    for record in records:
        collect_strings(record, found)

    print("\npath-like values remaining in the sanitized output:")
    for value in sorted(found):
        print(f"    {value}")


def main():
    if not RAW_DIR.is_dir():
        print(f"no raw captures at {RAW_DIR}", file=sys.stderr)
        return 1

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    everything = []

    for raw_file in sorted(RAW_DIR.glob("*.jsonl")):
        records = []
        for line in raw_file.read_text(encoding="utf-8").splitlines():
            if line.strip():
                records.append(sanitize(json.loads(line)))

        output_file = OUTPUT_DIR / raw_file.name
        with output_file.open("w", encoding="utf-8", newline="\n") as f:
            for record in records:
                f.write(json.dumps(record, ensure_ascii=False, sort_keys=True) + "\n")

        print(f"{raw_file.name}: {len(records)} records")
        everything.extend(records)

    report_path_values(everything)
    return 0


if __name__ == "__main__":
    sys.exit(main())
