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

# Hoisted out of the telemetry probe below, which used to be the only thing that computed it. The
# probe runs only when there is stdin to dump; the progress read has to work regardless, and a
# variable defined inside a block that did not run is silently $null rather than an error.
$supervisionRoot = Join-Path $env:USERPROFILE '.claude\supervision'

# How old a progress artefact may be before it is treated as absent. The app rewrites it at least
# once a minute while it is alive, so anything past this means the app is not running and the number
# would be a fossil. Comfortably above that heartbeat: this must never discard a good reading.
$progressMaxAgeSeconds = 300

# The ledger reading, ALREADY RENDERED BY THE APP. Nothing here parses PLAN.md or does arithmetic on
# it - the app computes, this renders, so the terminal and the owner's phone cannot disagree. In
# particular the percentage is truncated on the app side (75/76 must not read as 100%), which a
# `done * 100 / total` here would get wrong in the dangerous direction.
#
# EVERY failure path returns '' and the caller draws exactly the line it drew before this existed.
# A status line that throws or blanks is worse than one without a number.
function Get-ProgressSuffix($root, $id) {
    try {
        if (-not $id) { return '' }

        $file = Join-Path $root "$id\.progress.json"
        if (-not (Test-Path -LiteralPath $file)) { return '' }

        $age = ((Get-Date) - (Get-Item -LiteralPath $file).LastWriteTime).TotalSeconds
        if ($age -ge $progressMaxAgeSeconds) { return '' }

        $progress = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json

        # A file that parses but carries nothing usable is malformed for our purposes, not empty.
        if (-not $progress.text) { return '' }

        # No terminal width is available: the status line JSON carries model, workspace, cost and
        # context, and nothing about the window - verified against a real payload rather than
        # assumed. So "narrow" is judged by the length of the sentence itself, which grows with the
        # running / blocked / not-doing clauses, and the compact spelling comes from the numbers the
        # app already put in the file rather than from re-deriving anything here.
        if ($progress.text.Length -gt 40 -and $null -ne $progress.done -and $null -ne $progress.total) {
            return " $esc[90m$($progress.done)/$($progress.total) ($($progress.percent)%)$esc[0m"
        }

        return " $esc[90m$($progress.text)$esc[0m"
    } catch {
        return ''
    }
}

# --- Telemetry probe (best effort, never breaks the status line) ---
if ($role -and $raw) {
    try {
        $usageFile = $null
        if ($role -eq 'general') { $usageFile = Join-Path $supervisionRoot 'general\.usage.json' }
        elseif ($role -eq 'supervisor') { $usageFile = Join-Path $supervisionRoot "$orchId\.usage.json" }
        elseif ($role -eq 'communicator') { $usageFile = Join-Path $supervisionRoot "$orchId\.communicator.usage.json" }
        elseif ($role -in @('implementer','reviewer','solo')) { $usageFile = Join-Path $supervisionRoot "$orchId\$member\.usage.json" }
        if ($usageFile -and (Test-Path (Split-Path $usageFile))) {
            Set-Content -LiteralPath $usageFile -Value $raw -Encoding utf8
        }
    } catch { }
}

# --- Render ---
if ($role -eq 'supervisor') {
    Write-Output "$esc[1;91m SUPERVISOR $esc[0m$esc[31m $orchId $esc[0m $model$(Get-ProgressSuffix $supervisionRoot $orchId)"
}
elseif ($role -in @('implementer','reviewer','solo')) {
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
