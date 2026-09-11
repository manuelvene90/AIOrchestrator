# AI Orchestrator status line for Claude Code.
# 1) Renders the session's orchestration role so every terminal is identifiable at a glance:
#      red    SUPERVISOR · <display name, falling back to the orch-id>
#      blue   IMP-N · <display name, falling back to the orch-id>
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
$effort = ""
$cwd = ""
if ($null -ne $json) {
    try { $model = $json.model.display_name } catch { }
    try { $effort = $json.effort.level } catch { }
    try { $cwd = Split-Path -Leaf $json.workspace.current_dir } catch { }
}

$esc = [char]27
$role = $env:AIORCH_ROLE
$orchId = $env:AIORCH_ID
$member = $env:AIORCH_MEMBER

# Hoisted out of the telemetry probe below, which used to be the only thing that computed it. The
# probe runs only when there is stdin to dump; the progress read has to work regardless, and a
# variable defined inside a block that did not run is silently $null rather than an error.
$supervisionRoot = if ($env:AIORCH_SUPERVISION_ROOT) { $env:AIORCH_SUPERVISION_ROOT } else { Join-Path $env:USERPROFILE '.claude\supervision' }

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

# THE ORCHESTRATION'S NAME, ON EVERY ORCHESTRATED TERMINAL. The owner, 2026-08-21: *"since it's hard
# to change the terminal name, we should add the topic name to the custom claude status line which
# should be simpler I think"*. They are right, and it is strictly better than the window title this
# replaces in practice: a title is written once at spawn and a running window keeps the old one until
# it respawns, while this is re-rendered every turn, so a rename shows up on the very next line.
#
# It is the same string they read in their topic list, which is the whole point - the terminal and
# the phone name the session identically, so "the one called AI-Orch - away mode loop" means one
# thing in both places.
#
# IT REPLACES THE ID, IT DOES NOT SIT NEXT TO IT. This started life as Get-DisplayNameSuffix, an
# append-only helper, so a named orchestration rendered BOTH: "SUPERVISOR  strategy-lab-7  PB ·
# equity control bug". The owner, 2026-08-24: *"the new name doesn't replace the original unreadable,
# pointless default name like 'strategy-lab-7'. So the status bar gets unnecessarily longer. That
# name should be replaced and the new name should take its place while keeping its color (right now
# it's just being added, and it's white)"*. The id is the fallback, never a prefix to the name.
#
# AND THE COLOUR COMES FROM THE CALLER, which is the other half of what they asked for. The suffix
# carried a hardcoded ESC[97m bright white, and it could not have done better: it took only a root
# and an id, so it never knew whether it was being drawn on a red supervisor line, an orange solo
# one or a blue member one. Returning the bare LABEL puts it inside the caller's own colour run —
# each render line already opens one for the id and closes it after — so the name inherits the role
# colour by construction rather than by a parameter someone has to remember to pass.
#
# Same discipline as the progress suffix below, with the fallback moved: EVERY failure path returns
# the ID and the caller draws exactly the line it drew before names existed. An unnamed
# orchestration, a missing session.json, a half-written one - all of them mean "no name to show",
# never a broken status line and never an empty gap where the id used to be.
function Get-OrchLabel($root, $id) {
    try {
        if (-not $id) { return $id }

        $file = Join-Path $root "$id\session.json"
        if (-not (Test-Path -LiteralPath $file)) { return $id }

        $session = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json

        if (-not $session.displayName) { return $id }

        return $session.displayName
    } catch {
        return $id
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

# --- How full THIS session's context window is ---
# Every role gets it, because the owner asked to see it on every terminal (2026-08-21) and the
# session sitting in front of you is the one whose window you are about to fill.
#
# THE NUMBER IS CLAUDE CODE'S OWN and is never recomputed from the token fields beside it. The app
# reads the very same `used_percentage` out of the .usage.json this script has just written, so the
# terminal and the owner's phone quote one number. Deriving it here from input+cache tokens would be
# a second arithmetic that drifts the moment the payload gains a field.
#
# GREY AND UNCONDITIONAL, with no colour ramp at 80/90. Those two thresholds are
# ContextVisibility_Policy's, they decide who APPEARS on the Telegram surfaces, and copying them
# into a second language to tint a number is how two surfaces start disagreeing about what "high"
# means. The figure is the signal here; the surfaces that filter do the filtering.
#
# EVERY failure path leaves it '' and the caller draws exactly the line it drew before this existed:
# an older Claude Code with no context_window, a half-read payload, anything.
$contextSuffix = ''
if ($null -ne $json) {
    try {
        $contextPercent = $json.context_window.used_percentage
        if ($null -ne $contextPercent) {
            $contextSuffix = " $esc[90mctx $([int]$contextPercent)%$esc[0m"
        }
    } catch { }
}

# The effort level rides NEXT TO THE MODEL — "Fable 5.1 · xhigh" — because the owner asked to see it
# there (2026-09-09). Claude Code reports it as `effort.level` (low/medium/high/xhigh/max) and omits
# the block when the model has no such dial, so an absent value leaves the line exactly as it was.
$effortSuffix = ''
if ($effort) {
    $effortSuffix = " $esc[90m·$esc[0m $effort"
}

# --- Render ---
if ($role -eq 'supervisor') {
    Write-Output "$esc[1;91m SUPERVISOR $esc[0m$esc[31m $(Get-OrchLabel $supervisionRoot $orchId) $esc[0m $model$effortSuffix$contextSuffix$(Get-ProgressSuffix $supervisionRoot $orchId)"
}
elseif ($role -eq 'solo') {
    # THE SOLO CARRIES THE PROGRESS TOO, and it was the one role that did not. The suffix was wired
    # onto the supervisor line only, so in a BASIC orchestration — where there IS no supervisor and
    # the solo owns PLAN.md by its own role command — the ledger the app was faithfully writing to
    # .progress.json had nobody rendering it. The owner asked for this enrichment and then reported
    # it missing (2026-08-14), looking at a solo's terminal.
    #
    # Same call, not a copy of the logic: one renderer, so the terminal cannot disagree with the
    # owner's phone.
    # ORANGE, NOT BLUE. Blue is the MEMBER colour, and a solo is not a member of anything: it is the
    # session the owner is talking to, and it reads as an implementer at a glance. Owner, 2026-08-14:
    # *"the solo status bar has SOLO written in blue like an impl instead of in orange"*. 256-colour
    # 208 is the orange that matches the 🟠 this session already speaks with in the Telegram mirror,
    # so the two surfaces name the same voice the same way.
    $memberUpper = if ($member) { $member.ToUpper() } else { 'SOLO' }
    Write-Output "$esc[1;38;5;208m $memberUpper $esc[0m$esc[38;5;208m $(Get-OrchLabel $supervisionRoot $orchId) $esc[0m $model$effortSuffix$contextSuffix$(Get-ProgressSuffix $supervisionRoot $orchId)"
}
elseif ($role -in @('implementer','reviewer')) {
    # NOT the members: an implementer's terminal showing the orchestration's overall percentage would
    # invite it to reason about work that is not its own. The ledger belongs to whoever talks to the
    # owner.
    $memberUpper = if ($member) { $member.ToUpper() } else { 'IMPLEMENTER' }
    Write-Output "$esc[1;94m $memberUpper $esc[0m$esc[34m $(Get-OrchLabel $supervisionRoot $orchId) $esc[0m $model$effortSuffix$contextSuffix"
}
elseif ($role -eq 'communicator') {
    Write-Output "$esc[1;92m COMMUNICATOR $esc[0m$esc[32m $(Get-OrchLabel $supervisionRoot $orchId) $esc[0m $model$effortSuffix$contextSuffix"
}
elseif ($role -eq 'general') {
    Write-Output "$esc[1;93m GENERAL SUPERVISOR $esc[0m $model$effortSuffix$contextSuffix"
}
else {
    # A session the app did not spawn still has a context window, and the owner reads these too.
    Write-Output "$model$effortSuffix · $cwd$contextSuffix"
}
