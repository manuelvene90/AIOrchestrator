namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// What a channel file looked like from outside at one instant — its length and its last-write
/// stamp. <see cref="WasTaken"/> false means the stat could not be taken at all, which is reported
/// as UNKNOWN rather than rendered as a zero-length file.
/// </summary>
public readonly record struct ChannelFileSnapshot(bool WasTaken, long LengthBytes, DateTime LastWriteUtc);

/// <summary>
/// Whether anything wrote to a channel file WHILE the app was reading it.
///
/// <para>
/// WHY THIS EXISTS. Twice on 2026-08-13 a well-formed header was flagged as malformed on this
/// machine, and three hypotheses died: a missing blank line, a drifted second copy of the header
/// regex, and a read landing mid-append. The last one is refuted by the validator's own capture —
/// it stores the line it TESTED, and both captures were byte-identical to the finished line, where
/// a torn read would have stored a short one.
/// </para>
/// <para>
/// `imp-2` then named a writer nobody had ruled out: <see cref="Channel_Compactor"/> does not
/// append, it rewrites the live file WHOLESALE, which is a far wider window for a reader than an
/// append and the only writer that changes a line's position and content without the line ever
/// being edited. It is ruled out for those two events — that channel has no `.archive.md` sibling,
/// so it has never been compacted — but it is not ruled out for the next one, and a hypothesis
/// nobody can rule out from the log is one that gets re-argued from scratch by someone with less
/// evidence.
/// </para>
/// <para>
/// ONE STAMP WOULD ANSWER NOTHING. The question is not "how big is the file" but "did anything
/// write to it between the read and the report", and that needs two observations bracketing the
/// read. A single stamp at report time has nothing to be compared against.
/// </para>
/// <para>
/// WHAT UNCHANGED IS WORTH, stated so nobody promotes it later: length and last-write stamp both
/// unchanged is strong evidence that no writer touched the file across the read — not proof. A
/// rewrite to the identical length within the same stamp resolution would be invisible to it. The
/// verdict it earns is "no writer is implicated", never "no writer existed".
/// </para>
/// </summary>
public static class ChannelFile_Snapshot
{
    /// <summary>
    /// Never throws: a diagnostic that can take down the tick it is diagnosing is worse than no
    /// diagnostic. A file that cannot be stat-ed comes back UNKNOWN and says so in the log.
    /// </summary>
    public static ChannelFileSnapshot Take_OrUnknown(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);

            if (!info.Exists)
                return default;

            return new ChannelFileSnapshot(true, info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception)
        {
            return default;
        }
    }

    /// <summary>
    /// One log field, deliberately ASCII-only — this sits beside a hex dump in a report about bytes,
    /// and a separator whose own encoding could be questioned would be the sixth instrument this
    /// system has caught damaging what it measures.
    /// </summary>
    public static string Describe_ChangeAcrossRead(ChannelFileSnapshot beforeRead, ChannelFileSnapshot atReport)
    {
        // Decision 21's shape for a predicate that cannot be evaluated: say which half failed, never
        // invent a verdict. "UNKNOWN" with no cause is the silence this field exists to end.
        if (!beforeRead.WasTaken || !atReport.WasTaken)
            return $"file=UNKNOWN(could-not-stat:{(beforeRead.WasTaken ? "at-report" : "before-read")})";

        if (beforeRead.LengthBytes == atReport.LengthBytes && beforeRead.LastWriteUtc == atReport.LastWriteUtc)
            return $"file=UNCHANGED-ACROSS-READ({Describe_One(beforeRead)})";

        return $"file=CHANGED-DURING-READ({Describe_One(beforeRead)}->{Describe_One(atReport)})";
    }

    /// <summary>Round-trip ("O") on purpose: a stamp truncated to the minute cannot tell two writes apart.</summary>
    static string Describe_One(ChannelFileSnapshot snapshot)
    {
        return $"{snapshot.LengthBytes}B@{snapshot.LastWriteUtc:O}";
    }
}
