# AI Orchestrator — machine setup.
# Installs the aiorch PLUGIN (role protocols, hooks, channel helper), the status line, and the
# supervision home with its config. Safe to re-run: existing config values are kept unless you type
# new ones, and the plugin install is idempotent.
#
# THE ROLE PROTOCOLS ARE NOT COPIED ANY MORE. kit\ is a Claude Code plugin, installed once from a
# local marketplace pointing at this very checkout — which is what ends the four-derived-copies
# problem of decisions 17, 18 and 23.
# Run from the repo root:  powershell -ExecutionPolicy Bypass -File kit\install.ps1

$ErrorActionPreference = 'Stop'

$kitFolder = $PSScriptRoot
$claudeFolder = Join-Path $env:USERPROFILE '.claude'
$commandsFolder = Join-Path $claudeFolder 'commands'
$supervisionFolder = Join-Path $claudeFolder 'supervision'
$configFile = Join-Path $supervisionFolder 'config.json'
$secretsFile = Join-Path $supervisionFolder 'secrets.json'
$settingsFile = Join-Path $claudeFolder 'settings.json'
$statusLineTarget = Join-Path $supervisionFolder 'statusline.ps1'

Write-Host ''
Write-Host '=== AI Orchestrator setup ===' -ForegroundColor Cyan

# --- 0. Prerequisites -------------------------------------------------------------------------
$claudeCmd = Get-Command claude -ErrorAction SilentlyContinue
if ($null -eq $claudeCmd) {
    Write-Host 'WARNING: the "claude" CLI was not found on PATH. Install Claude Code first.' -ForegroundColor Yellow
}
$wtCmd = Get-Command wt -ErrorAction SilentlyContinue
if ($null -eq $wtCmd) {
    Write-Host 'NOTE: Windows Terminal (wt) not found — sessions will open in plain PowerShell windows.' -ForegroundColor Yellow
}

# jq — CHECKED HERE TOO, since 2026-09-10. install.sh has required it from the start; this file did
# not, and the asymmetry was invisible because nothing on Windows needed jq until the channel tool
# started reading its grammar with it. A Windows session runs that tool in msys bash, so a machine
# set up by THIS installer could pass every check and then refuse every typed entry.
#
# A WARNING, NOT AN EXIT, and the difference from install.sh is deliberate: there, jq merges
# settings.json and the install cannot proceed without it. Here it is the TOOL that needs jq, and a
# machine without it still gets a working app, working role commands, and untyped appends. Stopping
# the whole setup over a degraded path would be the harsher failure.
$jqCmd = Get-Command jq -ErrorAction SilentlyContinue
if ($null -eq $jqCmd) {
    Write-Host 'WARNING: jq was not found on PATH. Sessions can still append to channels, but the TYPED' -ForegroundColor Yellow
    Write-Host '         entries (--question/--option/--state/...) read the channel grammar with jq and will' -ForegroundColor Yellow
    Write-Host '         refuse until it is installed: winget install jqlang.jq   (or: choco install jq)' -ForegroundColor Yellow
    Write-Host '         Note that channel-append.sh runs in msys bash, so jq must be on the PATH that bash sees.' -ForegroundColor Yellow
}

# --- 1. Folders + role commands + status line -------------------------------------------------
New-Item -ItemType Directory -Force $commandsFolder | Out-Null
New-Item -ItemType Directory -Force $supervisionFolder | Out-Null
New-Item -ItemType Directory -Force (Join-Path $supervisionFolder '.requests') | Out-Null

# The kit is a PLUGIN. Registering this checkout as a local marketplace and installing from it means
# the installed copy is served from the files you are looking at — so "which copy is running" stops
# being a question anyone has to investigate (decisions 18 and 23). The version is READ BACK and
# printed, because the host asserts that exact number at startup.
if ($null -ne $claudeCmd) {
    & claude plugin marketplace add $kitFolder *> $null
    & claude plugin install 'aiorch@aiorch-local' --scope user -y *> $null

    # $ErrorActionPreference = 'Stop' does NOT stop on a NATIVE command's exit code in Windows
    # PowerShell 5.1, and the output is swallowed above — so without this check a failed install
    # still printed "Installed", on exactly the machine most likely to hit it.
    if ($LASTEXITCODE -ne 0) {
        Write-Host "claude plugin install exited $LASTEXITCODE — the aiorch plugin may NOT be installed. Re-run: claude plugin install aiorch@aiorch-local --scope user" -ForegroundColor Yellow
    } else {
        Write-Host 'Installed the aiorch plugin (role protocols, hooks, channel helper).' -ForegroundColor Green
    }

    $expectedVersion = (Get-Content (Join-Path $kitFolder '.claude-plugin\plugin.json') -Raw | ConvertFrom-Json).version
    $installed = $null
    try { $installed = (& claude plugin list --json | ConvertFrom-Json) | Where-Object { $_.id -eq 'aiorch@aiorch-local' } } catch { $installed = $null }

    # THE CONTENT IS COMPARED, NOT THE NUMBER — the twin of the block in install.sh, and the same
    # measurement behind it (2026-09-07, CLI 2.1.263): `claude plugin update` compares the version
    # STRING, so a commit that changes a role protocol without bumping plugin.json leaves the cached
    # copy untouched and reports "already at the latest version". Only uninstall-then-install refreshes
    # it. Compared by file hash rather than by commit, so UNCOMMITTED edits are caught too.
    function Get-KitContentSignature([string] $folder) {
        if ([string]::IsNullOrWhiteSpace($folder) -or -not (Test-Path -LiteralPath $folder)) { return $null }

        $root = (Resolve-Path -LiteralPath $folder).Path

        # Sorted by RELATIVE path so two folders are comparable, and the relative path is hashed with
        # the bytes: a file moved to another name is a different kit, and a hash of contents alone
        # would call that identical.
        $lines = Get-ChildItem -LiteralPath $root -Recurse -File | ForEach-Object {
            $relative = $_.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
            "$relative $((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)"
        } | Sort-Object

        return ($lines -join "`n")
    }

    $checkoutSignature = Get-KitContentSignature $kitFolder
    $installedSignature = Get-KitContentSignature $installed.installPath

    # THE HOST'S OWN VERIFIER READS gitCommitSha TOO (PluginVersion_Verifier), NOT ONLY THIS
    # SCRIPT'S file-hash compare — the twin of the block below and of install.sh. On the VPS on
    # 2026-09-07 a stage that touched no kit file still moved HEAD, the content compare read
    # identical (correctly — the text had not changed), and the host refused to spawn anyone over a
    # recorded commit it no longer recognised. So the installed record is compared too, and
    # reinstalls on its own even when the content compare found nothing.
    $checkoutSha = $null
    if ($null -ne (Get-Command git -ErrorAction SilentlyContinue)) {
        try { $gitSha = (& git -C $kitFolder rev-parse HEAD 2>$null); if ($LASTEXITCODE -eq 0) { $checkoutSha = $gitSha.Trim() } } catch { $checkoutSha = $null }
    }
    $installedSha = $null
    $installedPluginsFile = Join-Path $claudeFolder 'plugins\installed_plugins.json'
    if (Test-Path -LiteralPath $installedPluginsFile) {
        try {
            $installedRecord = (Get-Content -LiteralPath $installedPluginsFile -Raw | ConvertFrom-Json).plugins.'aiorch@aiorch-local'[0]
            $installedSha = $installedRecord.gitCommitSha
        } catch { $installedSha = $null }
    }
    $shaMismatch = (-not [string]::IsNullOrWhiteSpace($checkoutSha)) -and (-not [string]::IsNullOrWhiteSpace($installedSha)) -and ($installedSha -ne $checkoutSha)

    if ($null -eq $installedSignature -or $installedSignature -ne $checkoutSignature -or $shaMismatch) {
        $why =
            if ($null -eq $installedSignature) { 'its install path is missing' }
            elseif ($installedSignature -ne $checkoutSignature) { 'the installed copy differs from this checkout' }
            else { "the installed record's commit ($installedSha) differs from this checkout's HEAD ($checkoutSha) — the host's verifier reads this field even when the text compares identical" }
        Write-Host "aiorch: $why — REINSTALLING (an update would report success and change nothing)." -ForegroundColor Yellow

        & claude plugin uninstall aiorch *> $null
        & claude plugin install 'aiorch@aiorch-local' --scope user -y *> $null

        if ($LASTEXITCODE -ne 0) {
            Write-Host 'aiorch could NOT be reinstalled — sessions would read the OLD protocols.' -ForegroundColor Yellow
            Write-Host 'Run by hand: claude plugin uninstall aiorch; claude plugin install aiorch@aiorch-local --scope user -y' -ForegroundColor Yellow
        } else {
            Write-Host 'Reinstalled the aiorch plugin from this checkout.' -ForegroundColor Green
        }

        try { $installed = (& claude plugin list --json | ConvertFrom-Json) | Where-Object { $_.id -eq 'aiorch@aiorch-local' } } catch { $installed = $null }
        $installedSignature = Get-KitContentSignature $installed.installPath
    }

    # Re-checked rather than assumed: the reinstall can fail, and "reinstalled" printed over a cache
    # that did not move is the same lie one turn later.
    $contentState = if ($installedSignature -eq $checkoutSignature) {
        'content matches this checkout'
    } else {
        'CONTENT STILL DIFFERS from this checkout — sessions would read the old protocols'
    }

    if ($null -ne $installed -and $installed.version -eq $expectedVersion) {
        Write-Host "aiorch $($installed.version) is installed and enabled — $contentState." -ForegroundColor Green
    } else {
        Write-Host "aiorch reports version '$($installed.version)' but this checkout ships '$expectedVersion' ($contentState)." -ForegroundColor Yellow
        Write-Host 'Run: claude plugin update aiorch   (the host refuses to start sessions until they match)' -ForegroundColor Yellow
    }
} else {
    Write-Host 'Skipped the plugin install — the "claude" CLI is not on PATH. Sessions will have NO role protocols.' -ForegroundColor Yellow
}

# THE OLD HAND-INSTALLED KIT IS REMOVED, and this is not tidying: a local command in
# ~\.claude\commands WINS the slash word over a plugin skill (measured on CLI 2.1.263), so a
# leftover supervisor.md would be read INSTEAD of the plugin while `claude plugin list` reported the
# new version. Only the exact filenames this project ever shipped are touched.
$stale = @('supervisor.md','implementer.md','reviewer.md','solo.md','general-supervisor.md',
           'communicator.md','channel-append.sh','.installed-by.txt') |
    ForEach-Object { Join-Path $commandsFolder $_ }
$stale += @('supervisor-ledger-check.sh','run-to-the-end-check.sh','reviewer-readonly-check.sh',
            'supervisor-awaiting-answer-check.sh','hook-log.sh','hook-behaviour-check.sh',
            'watcher-behaviour-check.sh') |
    ForEach-Object { Join-Path (Join-Path $claudeFolder 'hooks') $_ }
# MOVED ASIDE, NEVER DELETED — the same rule as the app's own sweep (LegacyKit_Remover), and for the
# same reason: these names are generic, so a reviewer.md somebody wrote for something else can carry
# one. The rename breaks the shadow while leaving every byte on disk.
$moved = 0
foreach ($file in $stale) {
    if (Test-Path -LiteralPath $file) {
        Move-Item -LiteralPath $file -Destination "$file.aiorch-removed" -Force
        Write-Host "  moved aside: $file  ->  $file.aiorch-removed"
        $moved++
    }
}
if ($moved -gt 0) { Write-Host "Moved $moved hand-installed kit file(s) aside — they would have shadowed the plugin. Nothing was deleted." -ForegroundColor Green }

Copy-Item (Join-Path $kitFolder 'statusline\statusline.ps1') $statusLineTarget -Force
Write-Host 'Installed status line script.' -ForegroundColor Green

# --- 2. Status line in settings.json (backup first) -------------------------------------------
$settings = $null
if (Test-Path $settingsFile) {
    Copy-Item $settingsFile "$settingsFile.aiorch-backup" -Force
    try { $settings = Get-Content $settingsFile -Raw | ConvertFrom-Json } catch { $settings = $null }
}
if ($null -eq $settings) { $settings = New-Object PSObject }

$statusLineCommand = "powershell -NoProfile -ExecutionPolicy Bypass -File `"$statusLineTarget`""
$statusLineValue = New-Object PSObject
$statusLineValue | Add-Member -MemberType NoteProperty -Name 'type' -Value 'command'
$statusLineValue | Add-Member -MemberType NoteProperty -Name 'command' -Value $statusLineCommand

if ($settings.PSObject.Properties.Name -contains 'statusLine') {
    $settings.statusLine = $statusLineValue
} else {
    $settings | Add-Member -MemberType NoteProperty -Name 'statusLine' -Value $statusLineValue
}
$settings | ConvertTo-Json -Depth 10 | Out-File $settingsFile -Encoding utf8
Write-Host 'Configured Claude Code status line (previous settings backed up).' -ForegroundColor Green

# --- 3. Telegram configuration (skippable) -----------------------------------------------------
Write-Host ''
Write-Host 'Telegram setup (press Enter on any prompt to keep the current value / skip):' -ForegroundColor Cyan
Write-Host '  One-time manual steps, if not done yet:'
Write-Host '   1. Create a bot with @BotFather -> /newbot -> copy the token.'
Write-Host '   2. Create a NEW GROUP in Telegram, then in group settings enable "Topics".'
Write-Host '   3. Add your bot to the group as ADMIN (needs "Manage topics").'
Write-Host '   4. Group chat id: add @getidsbot to the group, it prints the -100... id (then remove it).'
Write-Host '   5. Your user id: message @userinfobot in a private chat.'
Write-Host ''

$existingConfig = $null
if (Test-Path $configFile) {
    try { $existingConfig = Get-Content $configFile -Raw | ConvertFrom-Json } catch { $existingConfig = $null }
}
$existingSecrets = $null
if (Test-Path $secretsFile) {
    try { $existingSecrets = Get-Content $secretsFile -Raw | ConvertFrom-Json } catch { $existingSecrets = $null }
}

$currentToken = if ($null -ne $existingSecrets) { $existingSecrets.telegramBotToken } else { $null }
$currentChatId = if ($null -ne $existingConfig) { $existingConfig.telegramSupergroupChatId } else { $null }
$currentOwnerId = if ($null -ne $existingConfig) { $existingConfig.telegramOwnerUserId } else { $null }

$tokenInput = Read-Host "Bot token [$(if ($currentToken) { 'kept' } else { 'not set' })]"
$chatIdInput = Read-Host "Supergroup chat id (-100...) [$(if ($currentChatId) { $currentChatId } else { 'not set' })]"
$ownerIdInput = Read-Host "Your Telegram user id [$(if ($currentOwnerId) { $currentOwnerId } else { 'not set' })]"

if ($tokenInput) { $currentToken = $tokenInput }
if ($chatIdInput) { $currentChatId = [long]$chatIdInput }
if ($ownerIdInput) { $currentOwnerId = [long]$ownerIdInput }

# --- 4. Write config.json (preserving repos) + secrets.json ------------------------------------
$repos = @()
if ($null -ne $existingConfig -and $null -ne $existingConfig.repos) { $repos = $existingConfig.repos }
if ($repos.Count -eq 0) {
    Write-Host ''
    Write-Host 'No repos configured yet. Add them now (empty name to finish):' -ForegroundColor Cyan
    while ($true) {
        $repoName = Read-Host 'Repo friendly name'
        if (-not $repoName) { break }
        $repoPath = Read-Host "Path of '$repoName'"
        if (-not (Test-Path -LiteralPath $repoPath -PathType Container)) {
            Write-Host "  Path does not exist, skipped." -ForegroundColor Yellow
            continue
        }
        $entry = New-Object PSObject
        $entry | Add-Member NoteProperty name $repoName
        $entry | Add-Member NoteProperty path $repoPath
        $repos = @($repos) + $entry
    }
}

$config = New-Object PSObject
$config | Add-Member NoteProperty repos $repos
$config | Add-Member NoteProperty supervisorModel $(if ($null -ne $existingConfig) { $existingConfig.supervisorModel } else { $null })
$config | Add-Member NoteProperty implementerModel $(if ($null -ne $existingConfig) { $existingConfig.implementerModel } else { $null })
$config | Add-Member NoteProperty generalSupervisorModel $(if ($null -ne $existingConfig -and $existingConfig.generalSupervisorModel) { $existingConfig.generalSupervisorModel } else { 'sonnet' })
$config | Add-Member NoteProperty telegramSupergroupChatId $currentChatId
$config | Add-Member NoteProperty telegramOwnerUserId $currentOwnerId
$config | ConvertTo-Json -Depth 10 | Out-File $configFile -Encoding utf8

$secrets = New-Object PSObject
$secrets | Add-Member NoteProperty telegramBotToken $currentToken
$secrets | ConvertTo-Json | Out-File $secretsFile -Encoding utf8

Write-Host ''
Write-Host 'Config written:' -ForegroundColor Green
Write-Host "  $configFile"
Write-Host "  $secretsFile  (bot token — never commit this anywhere)"
if ($null -eq $currentToken -or $null -eq $currentChatId -or $null -eq $currentOwnerId) {
    Write-Host 'Telegram is NOT fully configured — the orchestrator runs in file-only mode until it is.' -ForegroundColor Yellow
}

# --- 5. Build reminder -------------------------------------------------------------------------
Write-Host ''
Write-Host 'To build the orchestrator app this machine needs:' -ForegroundColor Cyan
Write-Host '  - .NET 10 SDK'
Write-Host '  - the Da-Vinci-Fintech-Suite repo checked out at ..\..\manuelvene90\Da-Vinci-Fintech-Suite (for LoggingLib)'
Write-Host 'Then:  dotnet build AIOrchestrator.slnx   and run AIOrchestrator\bin\Debug\net10.0-windows\AIOrchestrator.exe'
Write-Host ''
Write-Host '=== Setup complete ===' -ForegroundColor Cyan
