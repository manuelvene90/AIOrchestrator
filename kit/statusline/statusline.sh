#!/usr/bin/env bash
# AI Orchestrator status line for Claude Code — the bash twin of statusline.ps1, for macOS and
# Linux. SAME CONTRACT, LINE FOR LINE: the .ps1 is the reference and this must render the identical
# bytes for the identical input (StatusLineScriptParityTests pins that on shared fixtures). When one
# of the two changes, the other changes with it, or the terminal and the phone start disagreeing.
#
# 1) Renders the session's orchestration role so every terminal is identifiable at a glance:
#      red    SUPERVISOR · <display name, falling back to the orch-id>
#      blue   IMP-N · <display name, falling back to the orch-id>
#      amber  GENERAL SUPERVISOR
#    Falls back to model + cwd for non-orchestrated sessions.
# 2) TELEMETRY PROBE: for orchestrated sessions it dumps the RAW statusline JSON (cost, usage,
#    limits — whatever this Claude Code version provides) into the session's .usage.json, which
#    the orchestrator reads for per-member cost display and usage-limit Telegram alerts.
# Configured in ~/.claude/settings.json by install.sh / the host's kit installer; env vars are set
# by the spawner.
#
# Dependencies: bash (3.2 is enough — macOS ships it) and jq. No python3: decision 19 — the python3
# on a mixed machine may not see the paths bash hands it, and a status line must never depend on
# which of two interpreters answers to the name.

raw=""
raw=$(cat 2>/dev/null) || raw=""

# jq is the JSON parser here, as ConvertFrom-Json is there. Every read below goes through
# `json_get`, which returns '' for anything that is not a plain value — a missing key, a parse
# failure, a nested object — the same '' the .ps1's try/catch leaves behind.
json_get() {
    # $1: jq path expression; stdin: the document. Never fails, never prints null.
    printf '%s' "$raw" | jq -r "try ($1 | if . == null then \"\" elif type == \"object\" or type == \"array\" then \"\" else tostring end) catch \"\"" 2>/dev/null
}

json_valid=0
if [ -n "$raw" ] && printf '%s' "$raw" | jq -e . >/dev/null 2>&1; then
    json_valid=1
fi

model=""
cwd=""
if [ "$json_valid" = 1 ]; then
    model=$(json_get '.model.display_name')
    # Split-Path -Leaf: the last segment after either separator, trailing separators ignored —
    # the payload can carry a Windows path even when this script runs elsewhere.
    cwd_full=$(json_get '.workspace.current_dir')
    cwd=$(printf '%s' "$cwd_full" | sed -e 's#[\\/]*$##' -e 's#.*[\\/]##')
fi

esc=$(printf '\033')
role="${AIORCH_ROLE:-}"
orch_id="${AIORCH_ID:-}"
member="${AIORCH_MEMBER:-}"

# Hoisted out of the telemetry probe below, which used to be the only thing that computed it. The
# probe runs only when there is stdin to dump; the progress read has to work regardless.
supervision_root="${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}"

# How old a progress artefact may be before it is treated as absent. The app rewrites it at least
# once a minute while it is alive, so anything past this means the app is not running and the number
# would be a fossil. Comfortably above that heartbeat: this must never discard a good reading.
progress_max_age_seconds=300

# Seconds since a file was last written — GNU stat first, BSD stat second, '' when neither answers.
file_age_seconds() {
    local mtime now
    mtime=$(stat -c %Y "$1" 2>/dev/null || stat -f %m "$1" 2>/dev/null) || return 1
    now=$(date +%s)
    [ -n "$mtime" ] || return 1
    printf '%s' $((now - mtime))
}

# The ledger reading, ALREADY RENDERED BY THE APP. Nothing here parses PLAN.md or does arithmetic on
# it - the app computes, this renders, so the terminal and the owner's phone cannot disagree. In
# particular the percentage is truncated on the app side (75/76 must not read as 100%), which a
# `done * 100 / total` here would get wrong in the dangerous direction.
#
# EVERY failure path returns '' and the caller draws exactly the line it drew before this existed.
# A status line that throws or blanks is worse than one without a number.
get_progress_suffix() {
    local root="$1" id="$2" file age
    [ -n "$id" ] || return 0

    file="$root/$id/.progress.json"
    [ -f "$file" ] || return 0

    age=$(file_age_seconds "$file") || return 0
    [ "$age" -lt "$progress_max_age_seconds" ] || return 0

    # One jq pass renders the whole suffix: text.Length > 40 with done and total present gives the
    # compact spelling from the numbers the app already put in the file; otherwise the text itself.
    # No terminal width is available (the status line JSON carries model, workspace, cost and
    # context, nothing about the window), so "narrow" is judged by the length of the sentence.
    jq -r --arg esc "$esc" '
        try (
            if (.text | type) != "string" or .text == "" then ""
            elif (.text | length) > 40 and .done != null and .total != null then
                " \($esc)[90m\(.done)/\(.total) (\(.percent // "")%)\($esc)[0m"
            else
                " \($esc)[90m\(.text)\($esc)[0m"
            end
        ) catch ""' "$file" 2>/dev/null || true
}

# THE ORCHESTRATION'S NAME, ON EVERY ORCHESTRATED TERMINAL — the same string the owner reads in
# their topic list, so the terminal and the phone name the session identically. IT REPLACES THE ID,
# IT DOES NOT SIT NEXT TO IT, and it returns the bare LABEL so it inherits the caller's role colour.
# EVERY failure path returns the ID: an unnamed orchestration, a missing session.json, a half-written
# one — all of them mean "no name to show", never a broken status line and never an empty gap.
get_orch_label() {
    local root="$1" id="$2" file name
    [ -n "$id" ] || { printf '%s' "$id"; return 0; }

    file="$root/$id/session.json"
    [ -f "$file" ] || { printf '%s' "$id"; return 0; }

    name=$(jq -r 'try (if (.displayName | type) == "string" and .displayName != "" then .displayName else "" end) catch ""' "$file" 2>/dev/null) || name=""
    [ -n "$name" ] || { printf '%s' "$id"; return 0; }

    printf '%s' "$name"
}

# --- Telemetry probe (best effort, never breaks the status line) ---
if [ -n "$role" ] && [ -n "$raw" ]; then
    usage_file=""
    case "$role" in
        general)      usage_file="$supervision_root/general/.usage.json" ;;
        supervisor)   usage_file="$supervision_root/$orch_id/.usage.json" ;;
        communicator) usage_file="$supervision_root/$orch_id/.communicator.usage.json" ;;
        implementer|reviewer|solo) usage_file="$supervision_root/$orch_id/$member/.usage.json" ;;
    esac
    if [ -n "$usage_file" ] && [ -d "$(dirname "$usage_file")" ]; then
        printf '%s\n' "$raw" > "$usage_file" 2>/dev/null || true
    fi
fi

# --- How full THIS session's context window is ---
# THE NUMBER IS CLAUDE CODE'S OWN and is never recomputed from the token fields beside it; grey and
# unconditional, no colour ramp (those thresholds are ContextVisibility_Policy's, on the app side).
# The .ps1 casts with [int], which rounds HALF TO EVEN (8.5 -> 8, 9.5 -> 10); `rint` below is that
# rule spelled out so both scripts print the same digit. EVERY failure path leaves it ''.
context_suffix=""
if [ "$json_valid" = 1 ]; then
    context_percent=$(printf '%s' "$raw" | jq -r '
        def rint: floor as $f | (. - $f) as $frac
            | if $frac == 0.5 then (if ($f % 2) == 0 then $f else $f + 1 end)
              elif $frac > 0.5 then $f + 1 else $f end;
        try (.context_window.used_percentage
             | if . == null then "" else (tonumber | rint | tostring) end) catch ""' 2>/dev/null) || context_percent=""
    if [ -n "$context_percent" ]; then
        context_suffix=" ${esc}[90mctx ${context_percent}%${esc}[0m"
    fi
fi

to_upper() {
    printf '%s' "$1" | tr '[:lower:]' '[:upper:]'
}

# --- Render ---
case "$role" in
    supervisor)
        printf '%s\n' "${esc}[1;91m SUPERVISOR ${esc}[0m${esc}[31m $(get_orch_label "$supervision_root" "$orch_id") ${esc}[0m ${model}${context_suffix}$(get_progress_suffix "$supervision_root" "$orch_id")"
        ;;
    solo)
        # THE SOLO CARRIES THE PROGRESS TOO (it owns PLAN.md in a basic orchestration), in ORANGE —
        # 256-colour 208, the 🟠 this session speaks with in the Telegram mirror — never member blue.
        member_upper=$(to_upper "$member")
        [ -n "$member_upper" ] || member_upper="SOLO"
        printf '%s\n' "${esc}[1;38;5;208m ${member_upper} ${esc}[0m${esc}[38;5;208m $(get_orch_label "$supervision_root" "$orch_id") ${esc}[0m ${model}${context_suffix}$(get_progress_suffix "$supervision_root" "$orch_id")"
        ;;
    implementer|reviewer)
        # NOT the members: an implementer's terminal showing the orchestration's overall percentage
        # would invite it to reason about work that is not its own.
        member_upper=$(to_upper "$member")
        [ -n "$member_upper" ] || member_upper="IMPLEMENTER"
        printf '%s\n' "${esc}[1;94m ${member_upper} ${esc}[0m${esc}[34m $(get_orch_label "$supervision_root" "$orch_id") ${esc}[0m ${model}${context_suffix}"
        ;;
    communicator)
        printf '%s\n' "${esc}[1;92m COMMUNICATOR ${esc}[0m${esc}[32m $(get_orch_label "$supervision_root" "$orch_id") ${esc}[0m ${model}${context_suffix}"
        ;;
    general)
        printf '%s\n' "${esc}[1;93m GENERAL SUPERVISOR ${esc}[0m ${model}${context_suffix}"
        ;;
    *)
        # A session the app did not spawn still has a context window, and the owner reads these too.
        printf '%s\n' "${model} · ${cwd}${context_suffix}"
        ;;
esac
