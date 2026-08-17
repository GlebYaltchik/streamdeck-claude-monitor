<#
.SYNOPSIS
    Records a Claude Code hook payload for the reconnaissance phase.

.DESCRIPTION
    Reads the hook payload from stdin and appends it to .spike/raw/<event>.jsonl.
    Writes nothing to stdout and always exits 0, so it can never influence a
    session or block a tool call.

    Output is raw and may contain real paths and command text, which is why
    .spike/ is gitignored. Run sanitize-hooks.ps1 before committing anything.

.PARAMETER EventName
    The hook event this invocation belongs to, used to route the output file.
    The payload is expected to carry the same value in hook_event_name; both
    are recorded so they can be compared.
#>
param(
    [Parameter(Mandatory)]
    [string]$EventName
)

try {
    $payload = [Console]::In.ReadToEnd()
    $outputDirectory = Join-Path $PSScriptRoot '..\..\.spike\raw'
    if (-not (Test-Path $outputDirectory)) {
        New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    }

    $record = [ordered]@{
        capturedAt    = (Get-Date).ToUniversalTime().ToString('o')
        hookArgument  = $EventName
        projectDir    = $env:CLAUDE_PROJECT_DIR
        workingDir    = (Get-Location).Path
    }

    try {
        $record.payload = $payload | ConvertFrom-Json -Depth 40
        $record.payloadParsed = $true
    }
    catch {
        $record.payload = $payload
        $record.payloadParsed = $false
    }

    $line = $record | ConvertTo-Json -Depth 40 -Compress
    $file = Join-Path $outputDirectory "$EventName.jsonl"
    Add-Content -Path $file -Value $line -Encoding utf8
}
catch {
    # A recorder must never break a session. Swallow everything.
}

exit 0
