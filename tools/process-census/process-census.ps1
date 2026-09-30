<#
.SYNOPSIS
    Records, every few seconds, how many bash-family processes are alive and WHICH Claude session
    each one belongs to — so the next "bash.exe to CPU saturation" episode names its source.

.DESCRIPTION
    Written 2026-09-30 after the owner's machine became unusable with bash.exe processes from the Git
    install, and the evidence left on disk could show what was starving the machine but not who was
    spawning the processes. Everything here is read-only: one Win32_Process query per sample.

    Each sample appends ONE JSON line to the output file:
      ts, cpu (total %), bash (count of bash/sh/grep/... processes), bySession (count per owning
      Claude session, named by its role command line when it has one), and — only when the count is at
      or above -DetailThreshold — top (the most common command lines among them).

    A process is attributed by walking parent pids up to the nearest claude.exe/node.exe ancestor.
    An edge is followed only when the parent was created BEFORE the child, so a recycled pid cannot
    attribute a process to a stranger. Anything without such an ancestor is counted as "<no claude>";
    its root process name is recorded instead.

    Stop it with:  Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
                   Where-Object CommandLine -like '*process-census.ps1*' | ForEach-Object { Stop-Process -Id $_.ProcessId }

.PARAMETER IntervalSeconds
    Seconds between samples (default 15).

.PARAMETER DetailThreshold
    Bash-family count at which the sample also lists the most common command lines (default 25).

.PARAMETER OutFile
    Where the JSON lines go (default ~/.claude/supervision/diag/process-census.jsonl). Rotated to
    .1.jsonl past 20 MB, so it cannot fill the disk.
#>
param(
    [int]$IntervalSeconds = 15,
    [int]$DetailThreshold = 25,
    [string]$OutFile = (Join-Path $HOME '.claude\supervision\diag\process-census.jsonl')
)

$ErrorActionPreference = 'Stop'

$BashFamily = @('bash.exe', 'sh.exe', 'grep.exe', 'sed.exe', 'awk.exe', 'gawk.exe', 'cat.exe', 'md5sum.exe',
                'wc.exe', 'sleep.exe', 'cut.exe', 'tr.exe', 'find.exe', 'mkdir.exe', 'dirname.exe', 'jq.exe', 'python.exe')
$SessionRoots = @('claude.exe', 'node.exe')
$MaxBytes = 20MB

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutFile) | Out-Null

function Get-SessionLabel($process) {
    $line = [string]$process.CommandLine
    if ($line -match '/(supervisor|implementer|reviewer|solo|general-supervisor|communicator)\s+([^\s"'']+)') {
        return "$($Matches[1]) $($Matches[2]) (pid $($process.ProcessId))"
    }
    return "$($process.Name) pid $($process.ProcessId)"
}

function Append-Line([string]$line) {
    # NOT Add-Content -Encoding utf8: on Windows PowerShell 5.1 that writes a BOM at the start of a
    # new file, and the first JSON line then fails to parse in anything that reads it.
    [System.IO.File]::AppendAllText($OutFile, $line + "`n", (New-Object System.Text.UTF8Encoding($false)))
}

while ($true) {
    try {
        $all = Get-CimInstance Win32_Process
        $byPid = @{}
        foreach ($p in $all) { $byPid[[int]$p.ProcessId] = $p }

        $family = @($all | Where-Object { $BashFamily -contains $_.Name.ToLowerInvariant() })
        $bySession = @{}

        foreach ($p in $family) {
            $current = $p
            $label = $null
            $root = $p

            for ($depth = 0; $depth -lt 64; $depth++) {
                $parent = $byPid[[int]$current.ParentProcessId]
                if ($null -eq $parent -or $parent.CreationDate -gt $current.CreationDate) { break }
                if ($SessionRoots -contains $parent.Name.ToLowerInvariant()) { $label = Get-SessionLabel $parent; break }
                $current = $parent
                $root = $parent
            }

            if ($null -eq $label) { $label = "<no claude> root=$($root.Name)" }
            $bySession[$label] = 1 + [int]$bySession[$label]
        }

        $cpu = (Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'").PercentProcessorTime

        $sample = [ordered]@{
            ts        = (Get-Date).ToUniversalTime().ToString('o')
            cpu       = [int]$cpu
            bash      = $family.Count
            bySession = $bySession
        }

        if ($family.Count -ge $DetailThreshold) {
            $sample.top = @($family |
                Group-Object { $l = [string]$_.CommandLine; if ($l.Length -gt 200) { $l.Substring(0, 200) } else { $l } } |
                Sort-Object Count -Descending | Select-Object -First 10 |
                ForEach-Object { [ordered]@{ n = $_.Count; cmd = $_.Name } })
        }

        if ((Test-Path -LiteralPath $OutFile) -and (Get-Item -LiteralPath $OutFile).Length -gt $MaxBytes) {
            Move-Item -LiteralPath $OutFile -Destination ($OutFile -replace '\.jsonl$', '.1.jsonl') -Force
        }

        Append-Line (($sample | ConvertTo-Json -Compress -Depth 5))
    }
    catch {
        # A census that stops on its first bad sample would go silent exactly when the machine is
        # struggling, which is when it is needed. The failure is written as a sample of its own.
        $failure = [ordered]@{ ts = (Get-Date).ToUniversalTime().ToString('o'); error = "$($_.Exception.Message)" }
        try { Append-Line (($failure | ConvertTo-Json -Compress)) } catch { }
    }

    Start-Sleep -Seconds $IntervalSeconds
}
