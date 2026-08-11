# AI Orchestrator — machine setup.
# Installs the Claude Code role commands, the status line, and the supervision home with its
# config. Safe to re-run: existing config values are kept unless you type new ones.
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

# --- 1. Folders + role commands + status line -------------------------------------------------
New-Item -ItemType Directory -Force $commandsFolder | Out-Null
New-Item -ItemType Directory -Force $supervisionFolder | Out-Null
New-Item -ItemType Directory -Force (Join-Path $supervisionFolder '.requests') | Out-Null

# Every .md in kit\commands is a role command — copy them all, so a new role can never be
# added to the kit and silently never installed (reviewer.md and solo.md were missing here
# from the day they were written).
$roleCommands = Get-ChildItem (Join-Path $kitFolder 'commands') -Filter *.md
Copy-Item $roleCommands.FullName $commandsFolder -Force
$installedNames = ($roleCommands | ForEach-Object { '/' + $_.BaseName }) -join ', '
Write-Host "Installed $($roleCommands.Count) role commands: $installedNames" -ForegroundColor Green

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
