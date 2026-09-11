#!/usr/bin/env bash
# AI Orchestrator — machine setup for macOS and Linux, the twin of install.ps1. Does FOUR things:
# the supervision home and its folders, the aiorch PLUGIN (marketplace + install), the status line
# and its settings.json entry — then the optional Telegram configuration, kept unless you type new
# values. Safe to re-run: the plugin install is idempotent and every copy is content-compared (md5).
# Run from the repo root:  bash kit/install.sh
# The plugin cache is compared to this checkout by CONTENT and REINSTALLED when it differs: `claude
# plugin update` compares the version string only (measured 2026-09-07, CLI 2.1.263).
#
# THE ROLE PROTOCOLS ARE NOT COPIED ANY MORE. kit/ is a Claude Code plugin, installed once from a
# local marketplace pointing at this very checkout, and from then on every session loads it with no
# flag and no copy — which is what ends the four-derived-copies problem of decisions 17, 18 and 23.
# The running host no longer installs it either: it CHECKS the version and refuses to start sessions
# against a kit it was not built for.
# Dependencies: bash, jq (settings.json is merged, never rewritten blind), md5sum or md5, claude.

set -euo pipefail

kit_folder="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
claude_folder="$HOME/.claude"
commands_folder="$claude_folder/commands"
supervision_folder="$claude_folder/supervision"
config_file="$supervision_folder/config.json"
secrets_file="$supervision_folder/secrets.json"
settings_file="$claude_folder/settings.json"
status_line_target="$supervision_folder/statusline.sh"

say()  { printf '%s\n' "$*"; }
ok()   { printf '\033[32m%s\033[0m\n' "$*"; }
warn() { printf '\033[33m%s\033[0m\n' "$*"; }
# NOT named `head`: it used to be, and it SHADOWED /usr/bin/head for the whole script —
# the first `| head -1` added to this file silently returned the string '-1'.
heading() { printf '\033[36m%s\033[0m\n' "$*"; }

# md5sum (GNU, Linux) or md5 -q (BSD, macOS): the same digest, two spellings.
file_md5() {
    if command -v md5sum >/dev/null 2>&1; then md5sum "$1" | cut -d' ' -f1
    elif command -v md5 >/dev/null 2>&1; then md5 -q "$1"
    else cat "$1" | cksum | cut -d' ' -f1
    fi
}

# Copies only when the content differs; prints 1 when it copied, 0 when it was already current.
install_file() {
    local source="$1" target="$2"
    if [ -f "$target" ] && [ "$(file_md5 "$source")" = "$(file_md5 "$target")" ]; then
        printf '0'; return 0
    fi
    cp "$source" "$target"
    printf '1'
}

say ''
heading '=== AI Orchestrator setup ==='

# --- 0. Prerequisites -------------------------------------------------------------------------
if ! command -v jq >/dev/null 2>&1; then
    warn 'ERROR: jq is required (it merges ~/.claude/settings.json and writes config.json). Install it: brew install jq / apt install jq'
    exit 1
fi
if ! command -v claude >/dev/null 2>&1; then
    warn 'WARNING: the "claude" CLI was not found on PATH. Install Claude Code first.'
fi

# --- 1. Folders + role commands + status line -------------------------------------------------
mkdir -p "$commands_folder" "$supervision_folder" "$supervision_folder/.requests"

# The kit is a PLUGIN. Registering this checkout as a local marketplace and installing from it means
# the installed copy is served from the files you are looking at — so "which copy is running" stops
# being a question anyone has to investigate (decisions 18 and 23).
#
# Both commands are idempotent; re-running this script re-points the marketplace at this checkout,
# which is what you want after moving the repo. The version is READ BACK and printed, because the
# host asserts that exact number at startup and a silent install of the wrong one would only surface
# later as a refusal to spawn.
if command -v claude >/dev/null 2>&1; then
    # `|| { ...; }` and not `|| cmd | tail`: a pipe binds tighter than ||, so the branch written to
    # REPORT a failure was itself a pipeline whose non-zero status killed the script under pipefail.
    if ! claude plugin marketplace add "$kit_folder" >/dev/null 2>&1; then
        claude plugin marketplace add "$kit_folder" 2>&1 | tail -2 || true
    fi
    # One field of the installed record. `first(...)` rather than a pipe into `head`: with pipefail
    # set, head closing the pipe early is a SIGPIPE that fails the whole assignment. `|| true` on the
    # outside because an assignment takes the status of its substitution, and an older CLI without
    # --json, or one not logged in, would otherwise abort the installer silently between installing
    # the plugin and writing any configuration.
    plugin_field() {
        claude plugin list --json 2>/dev/null \
            | jq -r --arg id 'aiorch@aiorch-local' --arg field "$1" 'first(.[] | select(.id==$id) | .[$field]) // empty' 2>/dev/null \
            || true
    }

    # DRIFT FIXED IN PASSING, same family as the defect above: this block used to print "Installed the
    # aiorch plugin" whenever the command SUCCEEDED — which it does on every re-run — and, in green,
    # "The aiorch plugin was already installed" when it FAILED. So the one machine where the install
    # genuinely broke read as the ordinary case. Asked before, reported after.
    was_installed="$(plugin_field version)"

    if claude plugin install aiorch@aiorch-local --scope user -y >/dev/null 2>&1; then
        if [ -n "$was_installed" ]; then
            ok "The aiorch plugin was already installed ($was_installed)."
        else
            ok 'Installed the aiorch plugin (role protocols, hooks, channel helper).'
        fi
    else
        warn 'claude plugin install FAILED — sessions may have NO role protocols. The content check below says what is actually there.'
    fi

    installed_version="$(plugin_field version)"
    installed_path="$(plugin_field installPath)"
    expected_version="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p;/"version"/q' "$kit_folder/.claude-plugin/plugin.json" || true)"

    # THE CONTENT IS COMPARED, NOT THE NUMBER, and this is the 2026-09-07 lesson.
    #
    # MEASURED that day on CLI 2.1.263, in a throwaway Claude home: commit a change to the kit WITHOUT
    # bumping .claude-plugin/plugin.json, then `claude plugin update aiorch`. It answers "aiorch is
    # already at the latest version (1.0.0)", the cached files still hold the OLD text, and the
    # recorded gitCommitSha still names the old commit. `claude plugin marketplace update` first
    # changes nothing. ONLY uninstall-then-install refreshes either. That is precisely what happened on
    # the VPS: this script said "installed and enabled 1.0.0" and the daemon said "Kit check OK" over a
    # cache still holding stage 1c.
    #
    # The installed cache is a byte-for-byte copy of kit/ (measured), so `diff -rq` is the whole test —
    # and it catches what a commit comparison cannot: a checkout with UNCOMMITTED edits, where HEAD is
    # unchanged and the text is not.
    reinstall_reason=''
    if [ -z "$installed_path" ]; then
        reinstall_reason='the CLI reports no install path for it'
    elif [ ! -d "$installed_path" ]; then
        reinstall_reason="its recorded install path is gone ($installed_path)"
    elif ! diff -rq "$installed_path" "$kit_folder" >/dev/null 2>&1; then
        reinstall_reason='the installed copy differs from this checkout'
    fi

    # THE HOST'S OWN VERIFIER READS gitCommitSha TOO (PluginVersion_Verifier), NOT ONLY THIS
    # SCRIPT'S diff. On the VPS on 2026-09-07 a stage that touched no kit file still moved HEAD,
    # `diff -rq` above read identical (correctly — the text really had not changed), and the host
    # refused to spawn anyone over a recorded commit it no longer recognised. A content compare that
    # is silent about SHA cannot catch that, so it is compared here too and reinstalls on its own,
    # even when the byte-for-byte diff found nothing.
    if [ -z "$reinstall_reason" ] && command -v git >/dev/null 2>&1; then
        installed_plugins_file="$claude_folder/plugins/installed_plugins.json"
        if [ -f "$installed_plugins_file" ]; then
            checkout_sha="$(git -C "$kit_folder" rev-parse HEAD 2>/dev/null || true)"
            installed_sha="$(jq -r --arg id 'aiorch@aiorch-local' \
                '.plugins[$id][0].gitCommitSha // empty' "$installed_plugins_file" 2>/dev/null || true)"
            if [ -n "$checkout_sha" ] && [ -n "$installed_sha" ] && [ "$checkout_sha" != "$installed_sha" ]; then
                reinstall_reason="the installed record's commit ($installed_sha) differs from this checkout's HEAD ($checkout_sha) — the host's verifier reads this field even when the text compares identical"
            fi
        fi
    fi

    if [ -n "$reinstall_reason" ]; then
        warn "aiorch: $reinstall_reason — REINSTALLING (an update would report success and change nothing)."
        claude plugin uninstall aiorch >/dev/null 2>&1 || true
        if claude plugin install aiorch@aiorch-local --scope user -y >/dev/null 2>&1; then
            installed_version="$(plugin_field version)"
            installed_path="$(plugin_field installPath)"
            ok 'Reinstalled the aiorch plugin from this checkout.'
        else
            warn 'aiorch could NOT be reinstalled — sessions would read the OLD protocols.'
            warn 'Run by hand: claude plugin uninstall aiorch && claude plugin install aiorch@aiorch-local --scope user -y'
        fi
    fi

    # Re-checked rather than assumed: the reinstall above can fail, and "reinstalled" printed over a
    # cache that did not move is the same lie one turn later.
    if [ -n "$installed_path" ] && [ -d "$installed_path" ] && diff -rq "$installed_path" "$kit_folder" >/dev/null 2>&1; then
        content_state='content matches this checkout'
    else
        content_state='CONTENT STILL DIFFERS from this checkout — sessions would read the old protocols'
    fi

    if [ -n "$installed_version" ] && [ "$installed_version" = "$expected_version" ]; then
        ok "aiorch $installed_version is installed and enabled — $content_state."
    else
        warn "aiorch reports version '${installed_version:-none}' but this checkout ships '$expected_version' ($content_state)."
        warn "Run: claude plugin update aiorch   (the host refuses to start sessions until they match)"
    fi
else
    warn 'Skipped the plugin install — the "claude" CLI is not on PATH. Sessions will have NO role protocols.'
fi

# THE OLD HAND-INSTALLED KIT IS REMOVED, and this is not tidying: a local command in
# ~/.claude/commands WINS the slash word over a plugin skill (measured on CLI 2.1.263), so a
# leftover supervisor.md would be read INSTEAD of the plugin while `claude plugin list` reported the
# new version. Only the exact filenames this project ever shipped are touched.
# IT MOVES THEM ASIDE, IT DOES NOT DELETE THEM — the same rule as the app's own sweep
# (LegacyKit_Remover), and for the same reason: these names are GENERIC, so a `reviewer.md` or a
# `solo.md` somebody wrote for something else entirely can carry one, and unlinking it would destroy
# their work at a setup they ran for another purpose. The rename breaks the shadow just as completely
# — a command is resolved by its .md name and that name is gone — while leaving every byte on disk.
moved=0
move_aside() {
    [ -f "$1" ] || return 0
    mv -f "$1" "$1.aiorch-removed"
    moved=$((moved + 1))
    say "  moved aside: $1  ->  $1.aiorch-removed"
}
for stale in supervisor implementer reviewer solo general-supervisor communicator; do
    move_aside "$commands_folder/$stale.md"
done
for stale in channel-append.sh .installed-by.txt; do
    move_aside "$commands_folder/$stale"
done
for stale in supervisor-ledger-check.sh run-to-the-end-check.sh reviewer-readonly-check.sh \
             supervisor-awaiting-answer-check.sh hook-log.sh hook-behaviour-check.sh watcher-behaviour-check.sh; do
    move_aside "$claude_folder/hooks/$stale"
done
if [ "$moved" -gt 0 ]; then
    ok "Moved $moved hand-installed kit file(s) aside — they would have shadowed the plugin. Nothing was deleted."
fi

if [ "$(install_file "$kit_folder/statusline/statusline.sh" "$status_line_target")" = 1 ]; then
    ok 'Installed status line script.'
else
    ok 'Status line script already current.'
fi
chmod +x "$status_line_target"

# --- 2. Status line in settings.json (backup first) -------------------------------------------
# Same command shape StatusLineSettings_Wirer.Build_Command writes for a .sh script.
status_line_command="bash \"$status_line_target\""
if [ -f "$settings_file" ]; then
    if ! jq -e . "$settings_file" >/dev/null 2>&1; then
        # Unlike install.ps1, which starts over from an empty object, an unreadable settings file
        # is refused here: it is the user's to fix, and overwriting it would destroy their hooks.
        warn "ERROR: $settings_file is not valid JSON — refusing to rewrite it. Fix it and re-run."
        exit 1
    fi
    cp "$settings_file" "$settings_file.aiorch-backup"
    settings_json="$(cat "$settings_file")"
else
    settings_json='{}'
fi
printf '%s' "$settings_json" \
    | jq --arg command "$status_line_command" '.statusLine = {type: "command", command: $command}' \
    > "$settings_file.tmp" && mv "$settings_file.tmp" "$settings_file"
ok 'Configured Claude Code status line (previous settings backed up).'

# --- 3. Telegram configuration (skippable) -----------------------------------------------------
say ''
heading 'Telegram setup (press Enter on any prompt to keep the current value / skip):'
say '  One-time manual steps, if not done yet:'
say '   1. Create a bot with @BotFather -> /newbot -> copy the token.'
say '   2. Create a NEW GROUP in Telegram, then in group settings enable "Topics".'
say '   3. Add your bot to the group as ADMIN (needs "Manage topics").'
say '   4. Group chat id: add @getidsbot to the group, it prints the -100... id (then remove it).'
say '   5. Your user id: message @userinfobot in a private chat.'
say ''

existing_config='{}'
[ -f "$config_file" ] && jq -e . "$config_file" >/dev/null 2>&1 && existing_config="$(cat "$config_file")"
existing_secrets='{}'
[ -f "$secrets_file" ] && jq -e . "$secrets_file" >/dev/null 2>&1 && existing_secrets="$(cat "$secrets_file")"

current_token="$(printf '%s' "$existing_secrets" | jq -r '.telegramBotToken // empty')"
current_chat_id="$(printf '%s' "$existing_config" | jq -r '.telegramSupergroupChatId // empty')"
current_owner_id="$(printf '%s' "$existing_config" | jq -r '.telegramOwnerUserId // empty')"

# Prompts only on a terminal; a piped or scripted run keeps every current value.
if [ -t 0 ]; then
    read -r -p "Bot token [$([ -n "$current_token" ] && echo kept || echo 'not set')]: " token_input || token_input=""
    read -r -p "Supergroup chat id (-100...) [${current_chat_id:-not set}]: " chat_id_input || chat_id_input=""
    read -r -p "Your Telegram user id [${current_owner_id:-not set}]: " owner_id_input || owner_id_input=""
    [ -n "$token_input" ] && current_token="$token_input"
    [ -n "$chat_id_input" ] && current_chat_id="$chat_id_input"
    [ -n "$owner_id_input" ] && current_owner_id="$owner_id_input"
fi

# BOTH IDS ARE CHECKED BEFORE ANYTHING IS WRITTEN, exactly where install.ps1 checks them with
# [long]. They reach jq as `tonumber`, and jq exiting on a bad literal used to leave config.json
# TRUNCATED TO ZERO BYTES with no backup — repos, models and every Telegram setting gone, and the
# next start silently in file-only mode. A stray space or a `+` typed at the prompt was enough.
check_whole_number() {
    # $1 = human name, $2 = value. An empty value means "not set" and is fine; anything else must be
    # a whole number, optionally negative (supergroup ids are -100...).
    [ -n "$2" ] || return 0

    case "$2" in
        -*) [ -n "${2#-}" ] && [ -z "$(printf '%s' "${2#-}" | tr -d '0-9')" ] && return 0 ;;
        *)  [ -z "$(printf '%s' "$2" | tr -d '0-9')" ] && return 0 ;;
    esac

    warn "ERROR: the Telegram $1 must be a whole number, got '$2'. Nothing was written."
    exit 1
}

check_whole_number 'supergroup chat id' "$current_chat_id"
check_whole_number 'user id' "$current_owner_id"

# --- 4. Write config.json (preserving repos) + secrets.json ------------------------------------
repos="$(printf '%s' "$existing_config" | jq -c '.repos // []')"
if [ "$(printf '%s' "$repos" | jq 'length')" = 0 ] && [ -t 0 ]; then
    say ''
    heading 'No repos configured yet. Add them now (empty name to finish):'
    while true; do
        read -r -p 'Repo friendly name: ' repo_name || repo_name=""
        [ -n "$repo_name" ] || break
        read -r -p "Path of '$repo_name': " repo_path || repo_path=""
        if [ ! -d "$repo_path" ]; then
            warn '  Path does not exist, skipped.'
            continue
        fi
        repos="$(printf '%s' "$repos" | jq -c --arg name "$repo_name" --arg path "$repo_path" '. + [{name: $name, path: $path}]')"
    done
fi

# MERGED ONTO WHAT IS THERE, NEVER REBUILT FROM A FIELD LIST. The previous shape named six keys and
# silently dropped every other one the app persists — communicatorModel, voiceTranscribeCommand,
# orchestrationTokenBudget and telegramStatusScreenshots, which the owner toggles from their phone.
# A bootstrap re-run changed settings and said nothing. Written to a temp file and moved into place,
# after a backup, so a jq that fails for any reason leaves the existing config untouched rather than
# truncated.
[ -f "$config_file" ] && cp "$config_file" "$config_file.aiorch-backup"

printf '%s' "$existing_config" | jq \
    --argjson repos "$repos" \
    --arg chatId "$current_chat_id" \
    --arg ownerId "$current_owner_id" \
    '. + {
        repos: $repos,
        generalSupervisorModel: (.generalSupervisorModel // "sonnet"),
        telegramSupergroupChatId: (if $chatId == "" then null else ($chatId | tonumber) end),
        telegramOwnerUserId: (if $ownerId == "" then null else ($ownerId | tonumber) end)
    }' > "$config_file.tmp" && mv "$config_file.tmp" "$config_file" || {
        rm -f "$config_file.tmp"
        warn "ERROR: could not write $config_file — the existing file was left untouched."
        exit 1
    }

jq -n --arg token "$current_token" '{telegramBotToken: (if $token == "" then null else $token end)}' > "$secrets_file.tmp" \
    && mv "$secrets_file.tmp" "$secrets_file"
chmod 600 "$secrets_file"

say ''
ok 'Config written:'
say "  $config_file"
say "  $secrets_file  (bot token — never commit this anywhere)"
if [ -z "$current_token" ] || [ -z "$current_chat_id" ] || [ -z "$current_owner_id" ]; then
    warn 'Telegram is NOT fully configured — the orchestrator runs in file-only mode until it is.'
fi

# --- 5. Build reminder -------------------------------------------------------------------------
say ''
heading 'To build the headless host this machine needs the .NET 10 SDK. Then:'
say '  dotnet publish AIOrchestrator.Daemon -c Release -r <osx-arm64|linux-x64> --self-contained -o <install dir>'
say '  and install the service from deploy/launchd (macOS) or deploy/systemd (Linux) — see README-daemon.md'
say ''
heading '=== Setup complete ==='
