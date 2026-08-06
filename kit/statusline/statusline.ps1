# AI Orchestrator status line for Claude Code.
# 1) Renders the session's orchestration role so every terminal is identifiable at a glance:
#      red    SUPERVISOR · <orch-id>
#      blue   IMP-N · <orch-id>
#      amber  GENERAL SUPERVISOR
#    Falls back to model + cwd for non-orchestrated sessions.
# 2) TELEMETRY PROBE: for orchestrated sessions it dumps the RAW statusline JSON (cost, usage,
#    limits — whatever this Claude Code version provides) into the session's .usage.json, which
#    the orchestrator app reads for per-member cost display and usage-limit Telegram alerts.
# Configured in ~/.claude/settings.json by install.ps1; env vars are set by the spawner.

$raw = $null
$json = $null
try {
    $raw = [Console]::In.ReadToEnd()
    if ($raw) { $json = $raw | ConvertFrom-Json }
} catch { }

$model = ""
$cwd = ""
if ($null -ne $json) {
    try { $model = $json.model.display_name } catch { }
    try { $cwd = Split-Path -Leaf $json.workspace.current_dir } catch { }
}

$esc = [char]27
$role = $env:AIORCH_ROLE
$orchId = $env:AIORCH_ID
$member = $env:AIORCH_MEMBER

# --- Telemetry probe (best effort, never breaks the status line) ---
if ($role -and $raw) {
    try {
        $supervisionRoot = Join-Path $env:USERPROFILE '.claude\supervision'
        $usageFile = $null
        if ($role -eq 'general') { $usageFile = Join-Path $supervisionRoot 'general\.usage.json' }
        elseif ($role -eq 'supervisor') { $usageFile = Join-Path $supervisionRoot "$orchId\.usage.json" }
        elseif ($role -eq 'communicator') { $usageFile = Join-Path $supervisionRoot "$orchId\.communicator.usage.json" }
        elseif ($role -eq 'implementer') { $usageFile = Join-Path $supervisionRoot "$orchId\$member\.usage.json" }
        if ($usageFile -and (Test-Path (Split-Path $usageFile))) {
            Set-Content -LiteralPath $usageFile -Value $raw -Encoding utf8
        }
    } catch { }
}

# --- Render ---
if ($role -eq 'supervisor') {
    Write-Output "$esc[1;91m SUPERVISOR $esc[0m$esc[31m $orchId $esc[0m $model"
}
elseif ($role -eq 'implementer') {
    $memberUpper = if ($member) { $member.ToUpper() } else { 'IMPLEMENTER' }
    Write-Output "$esc[1;94m $memberUpper $esc[0m$esc[34m $orchId $esc[0m $model"
}
elseif ($role -eq 'communicator') {
    Write-Output "$esc[1;92m COMMUNICATOR $esc[0m$esc[32m $orchId $esc[0m $model"
}
elseif ($role -eq 'general') {
    Write-Output "$esc[1;93m GENERAL SUPERVISOR $esc[0m $model"
}
else {
    Write-Output "$model · $cwd"
}
