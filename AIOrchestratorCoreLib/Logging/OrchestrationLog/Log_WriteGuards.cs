using AIOrchestratorCoreLib.Storage;

namespace AIOrchestratorCoreLib.Logging.OrchestrationLog;

/// <summary>
/// The guards standing between <see cref="IOrchestrationLog"/> and the disk: a size cap, so an
/// unbounded writer can never BE the thing that fills the volume, and a low-disk policy, so the
/// record of an emergency survives while the chatter stops. Born from 2026-08-11, when this machine
/// reached 0 % free: the log had no rotation and no preflight anywhere, and its empty catch
/// discarded every line written in that window — the log erased the evidence of its own damage,
/// which is why the incident's evidence is so thin.
/// </summary>
public static class Log_WriteGuards
{
    /// <summary>
    /// Size at which a log file is rotated onto its single previous generation. Measured against the
    /// observed ~1.7 MB/day across all of this app's files: 8 MB is several days of history in the
    /// live file alone — enough to reconstruct an incident like the one above — while the bounded
    /// total of two generations, 16 MB, cannot matter next to a disk that 22 GB of build output
    /// filled. This is a durability bound, not a capacity fix.
    /// </summary>
    public const long MAX_LOG_BYTES = 8L * 1024 * 1024;

    /// <summary>
    /// Free space below which Info lines are dropped and only Warning/Error still reach disk.
    /// 512 MB is orders of magnitude above anything this log can consume between two probes, so the
    /// guard fires while writing still WORKS; a threshold set near zero would only take effect after
    /// the writes it exists to protect had already started failing.
    /// </summary>
    public const long LOW_DISK_THRESHOLD_BYTES = 512L * 1024 * 1024;

    /// <summary>
    /// Seconds between volume probes. A DriveInfo hit per log line is pure waste at this log's rate;
    /// 30 s bounds the blind window to a few hundred KB of Info lines, which is nothing against the
    /// 512 MB threshold, and rotation is unaffected because it never consults this cache.
    /// </summary>
    public const int DISK_PROBE_INTERVAL_SECONDS = 30;

    /// <summary>
    /// How much of a failure's text the single-line record keeps. Bounded on purpose: the recovery
    /// line must never become the unbounded buffer that a failing disk feeds.
    /// </summary>
    public const int MAX_FAILURE_MESSAGE_CHARS = 200;

    /// <summary>
    /// Whether an entry must be dropped because the volume is nearly full. A null
    /// <paramref name="freeBytes"/> means UNKNOWN and never blocks — that is
    /// <see cref="Disk_Space.Get_FreeBytes_OrNull"/>'s contract, and a probe that failed is not
    /// evidence of a full disk. Pure by design, so the policy is testable without depending on the
    /// test machine's actual free space.
    /// </summary>
    public static bool Should_Drop_Entry(long? freeBytes, LogLevels level)
    {
        if (freeBytes == null)
            return false;

        if (freeBytes.Value >= LOW_DISK_THRESHOLD_BYTES)
            return false;

        // Warning and Error ARE the record of the emergency; they are the last thing that may be
        // sacrificed for space, and they are rare enough that keeping them costs nothing.
        return level == LogLevels.Info;
    }

    /// <summary>
    /// The previous-generation path beside a log file: 'orchestrator-global.log.jsonl' becomes
    /// 'orchestrator-global.log.1.jsonl'.
    /// </summary>
    public static string Build_PreviousGenerationPath(string logFilePath)
    {
        var folder = Path.GetDirectoryName(logFilePath) ?? "";
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(logFilePath);
        var extension = Path.GetExtension(logFilePath);

        return Path.Combine(folder, $"{nameWithoutExtension}.1{extension}");
    }

    /// <summary>
    /// Renames an oversized log onto its single previous generation so the caller's append starts a
    /// fresh file, and reports whether that happened. NEVER throws and never deletes anything on its
    /// own: when the rename fails the caller keeps appending to the oversized file, because an
    /// oversized log is a far smaller problem than a dropped line and the next write retries.
    /// </summary>
    public static bool Rotate_IfNeeded(string logFilePath)
    {
        try
        {
            var file = new FileInfo(logFilePath);

            // One stat per append, deliberately uncached: the append that follows opens the same
            // file anyway, so this costs nothing measurable and rotation can never be late.
            if (!file.Exists || file.Length < MAX_LOG_BYTES)
                return false;

            // Overwrite: the older of the two generations is the one we can afford to lose, and
            // that is exactly what bounds the on-disk cost at 2x the cap.
            File.Move(logFilePath, Build_PreviousGenerationPath(logFilePath), overwrite: true);
            return true;
        }
        catch
        {
            // Not a silent swallow: 'false' is this method's answer, and the caller appends to the
            // un-rotated file rather than losing the line it was holding.
            return false;
        }
    }

    /// <summary>
    /// The single line reporting what the guards swallowed since the last such report, or null when
    /// they swallowed nothing. A guard that hides its own effect repeats the bug this class fixes.
    /// </summary>
    public static string? Build_RecoveryMessage_OrNull(
        int lowDiskDroppedCount,
        int writeFailureCount,
        string lastWriteFailureMessage)
    {
        List<string> parts = [];

        if (lowDiskDroppedCount > 0)
            parts.Add(
                $"dropped {lowDiskDroppedCount} {Describe_EntryCount(lowDiskDroppedCount)} "
                + $"while free disk space was below {LOW_DISK_THRESHOLD_BYTES / (1024 * 1024)} MB");

        if (writeFailureCount > 0)
            parts.Add(
                $"lost {writeFailureCount} {Describe_EntryCount(writeFailureCount)} to write failures, "
                + $"last failure: {Truncate_FailureMessage(lastWriteFailureMessage)}");

        if (parts.Count == 0)
            return null;

        return $"log guard recovered — {string.Join("; ", parts)}";
    }

    static string Describe_EntryCount(int count)
    {
        return count == 1 ? "entry" : "entries";
    }

    static string Truncate_FailureMessage(string message)
    {
        if (message.Length == 0)
            return "(no message recorded)";

        if (message.Length <= MAX_FAILURE_MESSAGE_CHARS)
            return message;

        return message[..MAX_FAILURE_MESSAGE_CHARS] + "…";
    }
}
