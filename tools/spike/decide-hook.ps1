<#
.SYNOPSIS
    Probes whether a PreToolUse hook can actually decide a permission request.

.DESCRIPTION
    Reads the hook payload from stdin and returns a permission decision only for
    Bash commands carrying a probe marker:

        claudedeck-probe-deny   -> deny
        claudedeck-probe-allow  -> allow

    Every other tool call is passed through untouched: the script prints nothing
    and exits 0, which leaves the session behaving exactly as it would without
    the hook. That containment is deliberate, because this runs inside a live
    working session.

    Also appends what it decided to .spike/raw/decisions.jsonl for the record.
#>

$ErrorActionPreference = 'Stop'

function Write-Probe($record) {
    try {
        $directory = Join-Path $PSScriptRoot '..\..\.spike\raw'
        if (-not (Test-Path $directory)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }
        $line = $record | ConvertTo-Json -Depth 10 -Compress
        Add-Content -Path (Join-Path $directory 'decisions.jsonl') -Value $line -Encoding utf8
    }
    catch {
    }
}

try {
    $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json -Depth 40
    $command = $payload.tool_input.command

    if ($payload.tool_name -ne 'Bash' -or -not $command) {
        exit 0
    }

    $decision = $null
    if ($command -match 'claudedeck-probe-deny') {
        $decision = 'deny'
    }
    elseif ($command -match 'claudedeck-probe-allow') {
        $decision = 'allow'
    }

    if (-not $decision) {
        exit 0
    }

    Write-Probe ([ordered]@{
        capturedAt     = (Get-Date).ToUniversalTime().ToString('o')
        decision       = $decision
        permissionMode = $payload.permission_mode
        toolUseId      = $payload.tool_use_id
    })

    $response = @{
        hookSpecificOutput = @{
            hookEventName            = 'PreToolUse'
            permissionDecision       = $decision
            permissionDecisionReason = "claudedeck probe: forced $decision"
        }
    }

    Write-Output ($response | ConvertTo-Json -Depth 10 -Compress)
}
catch {
    # Never break the session over a probe.
}

exit 0
