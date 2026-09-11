# AI Orchestrator bridge daemon — Windows service install (the headless alternative to the WPF app;
# the two exclude each other through the same instance lock, so run one or the other).
#
#   dotnet publish AIOrchestrator.Daemon -c Release -r win-x64 --self-contained -o C:\aiorchestrator
#   powershell -ExecutionPolicy Bypass -File deploy\windows\install-service.ps1 -BinaryPath C:\aiorchestrator\aiorchestrator-daemon.exe
#   powershell -ExecutionPolicy Bypass -File deploy\windows\install-service.ps1 -Uninstall
#
# The service runs as the OWNER's account (prompted), never LocalSystem: the sessions it spawns run
# `claude` with that account's login and %USERPROFILE%\.claude. Restart-on-failure is configured
# through `sc failure` (5 s, 10 s, 30 s; counter reset after a day). Elevated prompt required.
# UNVERIFIED on a real machine as of 2026-09-06 — written from the sc.exe / New-Service contracts.

[CmdletBinding()]
param(
    [string]$ServiceName = 'AIOrchestrator',
    [string]$BinaryPath = 'C:\aiorchestrator\aiorchestrator-daemon.exe',
    [string]$Account = "$env:USERDOMAIN\$env:USERNAME",
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

$isElevated = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isElevated) { throw 'Run this from an elevated (Administrator) PowerShell.' }

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($Uninstall) {
    if ($null -eq $existing) { Write-Host "Service '$ServiceName' is not installed."; return }
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }
    & sc.exe delete $ServiceName | Out-Null
    Write-Host "Service '$ServiceName' removed."
    return
}

if (-not (Test-Path -LiteralPath $BinaryPath)) { throw "Daemon binary not found at '$BinaryPath'. Publish it first (see README-daemon.md)." }

if ($null -eq $existing) {
    $credential = Get-Credential -UserName $Account -Message "Password for the account the orchestrator runs as ($Account) — it needs a Claude Code login in its own profile."
    New-Service -Name $ServiceName `
        -BinaryPathName "`"$BinaryPath`"" `
        -DisplayName 'AI Orchestrator' `
        -Description 'Telegram bridge and session watchdog for AI Orchestrator (headless host).' `
        -StartupType Automatic `
        -Credential $credential | Out-Null
    Write-Host "Service '$ServiceName' created for '$BinaryPath' running as $Account."
} else {
    # THE PATH IS RE-APPLIED, NOT ASSUMED. Skipping this is how a service keeps running last month's
    # binary while its owner reads "already exists" and believes the publish took effect — decision
    # 23's failure, in the one script written to fix it. sc.exe is used rather than Set-Service so
    # the quoting matches the New-Service call above.
    $currentPath = (Get-CimInstance Win32_Service -Filter "Name='$ServiceName'").PathName

    if ($currentPath -ne "`"$BinaryPath`"") {
        Write-Host "Service '$ServiceName' exists but points at $currentPath — repointing it at `"$BinaryPath`"."
        if ($existing.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }
        & sc.exe config $ServiceName binPath= "`"$BinaryPath`"" | Out-Null
    } else {
        Write-Host "Service '$ServiceName' already exists and points at the given binary — updating restart policy and starting it."
    }
}

# Restart on failure: three escalating delays, counter reset after 24 h. The daemon exits non-zero
# when its engine dies, so this is what brings the bridge back.
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
& sc.exe failureflag $ServiceName 1 | Out-Null

Start-Service -Name $ServiceName
Write-Host "Service '$ServiceName' started. Logs: Event Viewer (Application) and %USERPROFILE%\.claude\supervision\orchestrator-global.log.jsonl"
