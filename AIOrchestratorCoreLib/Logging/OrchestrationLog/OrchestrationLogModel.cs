using System.Globalization;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Logging.OrchestrationLog;

internal sealed class OrchestrationLogModel(ISupervisionPaths paths) : IOrchestrationLog
{
    readonly ISupervisionPaths _paths = paths;
    readonly Lock _writeLock = new();

    // Every field below is read and written under _writeLock only — they are the guards' memory of
    // what they swallowed, and the next successful write reports on them.
    long? _freeBytesAtLastProbe;
    DateTime _lastDiskProbeUtc = DateTime.MinValue;
    int _lowDiskDroppedCount;
    int _writeFailureCount;
    string _lastWriteFailureMessage = "";

    public event Action<IOrchestrationLogEntry>? EntryLogged;

    public void Log_Info(string orchId, string message)
    {
        Write_Entry(OrchestrationLogEntry_Factory.Create(DateTime.UtcNow, orchId, LogLevels.Info, message));
    }

    public void Log_Warning(string orchId, string message)
    {
        Write_Entry(OrchestrationLogEntry_Factory.Create(DateTime.UtcNow, orchId, LogLevels.Warning, message));
    }

    public void Log_Error(string orchId, string message, Exception? exception)
    {
        var fullMessage = exception == null
            ? message
            : $"{message} — {exception.GetType().Name}: {exception.Message}";

        Write_Entry(OrchestrationLogEntry_Factory.Create(DateTime.UtcNow, orchId, LogLevels.Error, fullMessage));
    }

    /// <summary>
    /// The single write path. EntryLogged fires for EVERY entry whether or not it reached disk: the
    /// UI panel is fed by that event and has to stay live exactly when the disk is the problem.
    /// </summary>
    void Write_Entry(IOrchestrationLogEntry entry)
    {
        IOrchestrationLogEntry? recoveryEntry = null;

        try
        {
            lock (_writeLock)
            {
                if (Should_Drop_ForLowDisk_Locked(entry.Level))
                    _lowDiskDroppedCount++;
                else
                {
                    // Written before the entry that triggered it, so it sits at the front of the gap
                    // it explains rather than after it.
                    recoveryEntry = Take_RecoveryEntry_OrNull_Locked(entry.OrchId);

                    if (Try_Append_ToFiles_Locked(entry))
                    {
                        _writeFailureCount = 0;
                        _lastWriteFailureMessage = "";
                    }
                    else
                        _writeFailureCount++;
                }
            }
        }
        catch (Exception exception)
        {
            // Unreachable by design — every helper above is total — but logging must never take the
            // bridge down. Recorded rather than swallowed: the next successful write reports it.
            Record_UnexpectedFailure(exception);
        }

        Publish_Entry_OrNull(recoveryEntry);
        Publish_Entry_OrNull(entry);
    }

    /// <summary>
    /// Whether the volume is too full to spend on this entry. The probe result is cached for
    /// <see cref="Log_WriteGuards.DISK_PROBE_INTERVAL_SECONDS"/> because a DriveInfo hit per log
    /// line is pure waste; the stale window costs at most a few hundred KB of Info lines, far below
    /// the threshold itself.
    /// </summary>
    bool Should_Drop_ForLowDisk_Locked(LogLevels level)
    {
        var now = DateTime.UtcNow;

        if (now - _lastDiskProbeUtc >= TimeSpan.FromSeconds(Log_WriteGuards.DISK_PROBE_INTERVAL_SECONDS))
        {
            _freeBytesAtLastProbe = Disk_Space.Get_FreeBytes_OrNull(_paths.Root);
            _lastDiskProbeUtc = now;
        }

        return Log_WriteGuards.Should_Drop_Entry(_freeBytesAtLastProbe, level);
    }

    /// <summary>
    /// Builds, WRITES and returns the one line reporting what the guards swallowed, or null when
    /// there is nothing to report or it could not be written. Only a recovery line that actually
    /// reached disk clears the counters, and only that one is returned for publishing — the line is
    /// a claim about the disk record, so an unwritten one would be a second lie on top of the first.
    /// </summary>
    IOrchestrationLogEntry? Take_RecoveryEntry_OrNull_Locked(string orchId)
    {
        var message = Log_WriteGuards.Build_RecoveryMessage_OrNull(
            _lowDiskDroppedCount,
            _writeFailureCount,
            _lastWriteFailureMessage);

        if (message == null)
            return null;

        // Built here rather than through Log_Warning on purpose: the public entry points come back
        // through Write_Entry, and a recovery line re-entering the write path would recurse.
        var recoveryEntry = OrchestrationLogEntry_Factory.Create(
            DateTime.UtcNow,
            orchId,
            LogLevels.Warning,
            message);

        if (!Try_Append_ToFiles_Locked(recoveryEntry))
            return null;

        _lowDiskDroppedCount = 0;
        _writeFailureCount = 0;
        _lastWriteFailureMessage = "";

        return recoveryEntry;
    }

    /// <summary>
    /// Appends one entry to the global log and, when the orchestration folder exists, to that
    /// orchestration's own log — rotating either file first when it has reached the cap. Reports
    /// whether the write happened; never throws, and never swallows silently: the failure's message
    /// is kept for the recovery line that the next successful write emits.
    /// </summary>
    bool Try_Append_ToFiles_Locked(IOrchestrationLogEntry entry)
    {
        try
        {
            var line = Build_JsonLine(entry);

            Directory.CreateDirectory(_paths.Root);

            Log_WriteGuards.Rotate_IfNeeded(_paths.GlobalLogFile);
            File.AppendAllText(_paths.GlobalLogFile, line);

            if (entry.OrchId.Length > 0 && Directory.Exists(_paths.Get_OrchestrationFolder(entry.OrchId)))
            {
                var orchestrationLogFile = _paths.Get_OrchestrationLogFile(entry.OrchId);

                Log_WriteGuards.Rotate_IfNeeded(orchestrationLogFile);
                File.AppendAllText(orchestrationLogFile, line);
            }

            return true;
        }
        catch (Exception exception)
        {
            _lastWriteFailureMessage = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    void Record_UnexpectedFailure(Exception exception)
    {
        lock (_writeLock)
        {
            _writeFailureCount++;
            _lastWriteFailureMessage = $"{exception.GetType().Name}: {exception.Message}";
        }
    }

    void Publish_Entry_OrNull(IOrchestrationLogEntry? entry)
    {
        if (entry == null)
            return;

        try
        {
            EntryLogged?.Invoke(entry);
        }
        catch
        {
            // Deliberately terminal: a faulty UI subscriber must not take the bridge down, and there
            // is no channel below this one to report a broken reporter to.
        }
    }

    static string Build_JsonLine(IOrchestrationLogEntry entry)
    {
        var node = new JsonObject
        {
            ["ts"] = entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
            ["orch"] = entry.OrchId,
            ["level"] = entry.Level.ToString(),
            ["message"] = entry.Message,
        };

        return node.ToJsonString() + Environment.NewLine;
    }
}
