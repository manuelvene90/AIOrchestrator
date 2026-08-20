using AIOrchestratorCoreLib.Limits;
using AIOrchestratorCoreLib.Usage;

namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// Is a session working RIGHT NOW, and has it picked up what we said to it? Its status-line probe
/// hands us the exact transcript path, and <see cref="TranscriptActivity_Reader"/> reads what that
/// transcript SAYS.
///
/// <para>
/// IT USED TO READ THE TRANSCRIPT'S MTIME, and that was the defect. The app nudges by writing to a
/// member's channel; the write fires the member's watcher; the watcher enqueues a notification; and
/// the enqueue writes a record, moving the mtime. So a session that ignored every wake looked busy
/// BECAUSE it was being woken. Measured on `rev-2` on 2026-08-13: nudged 13:49:09.422Z, mtime moved
/// 13:49:10.343Z, and six minutes later the escalation read that as "alive, its monitor works". Six
/// sessions sat deaf for two and a half hours behind it. An enqueue is now never activity.
/// </para>
///
/// <para>
/// A session cannot observe its own dormancy, and channel silence proves nothing — the protocol
/// forbids acknowledgment-only entries, so a live, obedient session with nothing to say writes
/// nothing at all. What separates that session from a deaf one is that it DEQUEUED: the app can
/// cause an enqueue, it cannot cause a dequeue, which makes the dequeue a read receipt.
/// </para>
/// </summary>
public static class SessionActivity_Probe
{
    /// <summary>Transcript freshness that counts as "working right now".</summary>
    public const int MIDTURN_SECONDS = 120;

    /// <summary>
    /// When this session last did anything ITSELF — a turn, a tool call, or picking up a queued
    /// wake. Null when we cannot tell, which callers must read as "no opinion", never as death.
    /// </summary>
    public static DateTime? Get_LastActivityUtc_OrNull(string usageFilePath)
    {
        return Read_Activity(usageFilePath).LastActivityUtc;
    }

    /// <summary>
    /// A wake this session was handed and has not picked up, and how long it has sat there. Null
    /// when nothing is outstanding — including for a session nobody has written to, which is what
    /// makes a legitimately quiet member safe by MECHANISM rather than by trusting the marker it
    /// wrote about itself.
    /// </summary>
    public static DateTime? Get_OldestUnansweredWakeUtc_OrNull(string usageFilePath)
    {
        return Read_Activity(usageFilePath).OldestUnansweredWakeUtc;
    }

    /// <summary>
    /// True only when this session was handed a wake it has not picked up for at least
    /// <paramref name="thresholdMinutes"/>, AND we could actually see its activity. Everything else
    /// — an unreadable transcript, a window that did not reach back far enough, a session nobody has
    /// written to — is false, because a respawn destroys in-context reasoning that exists in exactly
    /// one place and no state but a positive determination may trigger one.
    /// </summary>
    public static bool Is_DeafToWakes(string usageFilePath, DateTime nowUtc, int thresholdMinutes)
    {
        return TranscriptActivity_Reader.Is_DeafToWakes(Read_Activity(usageFilePath), nowUtc, thresholdMinutes);
    }

    /// <summary>
    /// Working right now. Shared with the UI's chips and the Telegram status line, so "working now"
    /// means one thing everywhere — the reason the fifteen readers of this function move together
    /// rather than one at a time. Two liveness clocks disagreeing is how this subsystem got here.
    /// </summary>
    public static bool Is_MidTurn(string usageFilePath)
    {
        var activity = Read_Activity(usageFilePath);

        // INSIDE A COMMAND IS MID-TURN, however long the command takes, and this clause is the
        // whole point of the flag. A session running one long build or test writes NOTHING to its
        // transcript while it runs, so the freshness test below cannot tell it from a dead one —
        // and the orphan detector, reading that silence, respawned a working session and destroyed
        // its context (owner, 2026-08-20: "I was seeing it, it was not unresponsive, I'm pretty
        // sure it was still working"). An unanswered tool call is positive evidence of work.
        if (activity.HasOpenToolCall)
            return true;

        var lastActivityUtc = activity.LastActivityUtc;

        if (lastActivityUtc == null)
            return false;

        return (DateTime.UtcNow - lastActivityUtc.Value).TotalSeconds < MIDTURN_SECONDS;
    }

    /// <summary>
    /// The one place that turns a usage file into an activity reading.
    ///
    /// <para>
    /// The transcript path comes from the status line's own payload. When it is absent or the file
    /// has gone, we fall back to the usage file's MTIME — the status line rewrites it on every
    /// render, so it still says something about turns being taken. That fallback is honest here in a
    /// way the old transcript-mtime read was not: nothing the APP does rewrites a session's usage
    /// file, so it cannot be moved by our own nudge. It carries no wake information, so it can only
    /// ever report activity, never deafness — which is the safe direction.
    /// </para>
    /// </summary>
    static TranscriptActivity_Reader.TranscriptActivity Read_Activity(string usageFilePath)
    {
        try
        {
            if (!File.Exists(usageFilePath))
                return TranscriptActivity_Reader.TranscriptActivity.Unknown;

            var rawJson = UsageTotals_Reader.Read_Text_Safe(usageFilePath);
            var transcriptPath = RateLimits_Reader.Read_TranscriptPath_OrNull(rawJson);

            if (transcriptPath != null && File.Exists(transcriptPath))
                return TranscriptActivity_Reader.Read(transcriptPath);

            return new TranscriptActivity_Reader.TranscriptActivity(
                File.GetLastWriteTimeUtc(usageFilePath),
                OldestUnansweredWakeUtc: null,
                SawActivity: true);
        }
        catch
        {
            return TranscriptActivity_Reader.TranscriptActivity.Unknown;
        }
    }
}
