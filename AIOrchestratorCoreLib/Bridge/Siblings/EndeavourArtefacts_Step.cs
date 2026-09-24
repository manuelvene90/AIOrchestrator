using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Bridge.Siblings;

/// <summary>
/// THE TICK'S RECONCILE OF EVERY ENDEAVOUR ARTEFACT (spec 2026-09-23 §3.3 compaction, §3.4, §7.4), called once
/// per mirror tick with the tick's session snapshot, open and closed alike:
/// <list type="bullet">
/// <item><c>.siblings</c> and <c>ENDEAVOUR.md</c> for every LINKED session — written for an open one with open
/// siblings, removed for anything else. Closed linked sessions are visited too, because that is how a file a
/// crash left behind is removed on the first tick back, by the same code as every other tick (§7.4). Unlinked
/// sessions are not visited at all: they cannot own these files (final review M3, see the loop).</item>
/// <item>COMPACTION of every open linked orchestration's outbox, through <see cref="Compact_Outboxes"/> — a
/// separate call the engine places after the poll, for the owner's lock allowance. The outbox is not tailed, so the tailer's
/// compaction step never sees it; the NO-GUARD overload of <see cref="Channel_Compactor.Compact_IfNeeded(string)"/>
/// is safe here because no tailer cursor exists to re-anchor, and the print runner's cursor is by entry
/// IDENTITY ("PENDING IS DECIDED BY IDENTITY"), which compaction does not break. A short outbox is answered
/// from its length without being opened.</item>
/// </list>
///
/// <para>
/// BOUNDED PER TICK. Each open linked orchestration's inputs (PLAN.md, two cached channel histories, one HEAD
/// file) are read ONCE and shared by every digest that lists it — never once per pair. No git process, and
/// nothing outside the linked orchestrations' own folders and working trees is read.
/// </para>
/// <para>
/// NEVER THROWS. Every file operation is inside its own try/catch, and a failure comes back as a line for the
/// caller's log naming the file — so a locked <c>ENDEAVOUR.md</c> costs one reconcile of one file, and the
/// tick, and every other file in it, carry on.
/// </para>
/// </summary>
public static class EndeavourArtefacts_Step
{
    public static IReadOnlyList<string> Reconcile(ISupervisionPaths paths, IReadOnlyList<IOrchestrationSession> sessions)
    {
        List<string> failures = [];

        var openLinked = sessions
            .Where(session => session.ClosedUtc == null && session.EndeavourId != null)
            .OrderBy(session => session.OrchId, StringComparer.Ordinal)
            .ToList();

        var inputs = Read_InputsOnce(paths, openLinked, failures);

        // LINKED SESSIONS ONLY, open and closed (final review M3, 2026-09-24). Every session used to be visited
        // — two stats each, every 2 s, ~250 on the owner's machine of 124 folders — for files that only a
        // linked session can own: they are written only for one, and v1 never unlinks, so there is no
        // "just unlinked" session whose files need removing. A closed linked session is still visited, which
        // is how its files go on the tick after its close and a crash's leftovers on the first tick back.
        foreach (var session in sessions.Where(session => session.EndeavourId != null))
        {
            var siblings = Open_Siblings_Of(session, openLinked);

            Reconcile_List(paths, session, siblings, failures);
            Reconcile_Digest(paths, session, siblings, inputs, failures);
        }

        return failures;
    }

    /// <summary>
    /// COMPACTION IS ITS OWN CALL, not part of <see cref="Reconcile"/> (Task 10b, from the Task 10 review). It
    /// takes the channel lock with a 1 s budget, and the reconcile runs BEFORE the owner's delivery — so a
    /// sibling that died holding <c>sibling-outbox.md.lock</c> (broken only after 60 s) spent 1 s of the tick's
    /// 1.5 s allowance on every tick for half a minute, and the owner's own delivery got what was left. The
    /// engine's rule is that the owner's message goes first in that allowance; the engine calls this after the
    /// poll, where the tailed channels' own compaction already runs.
    /// </summary>
    public static IReadOnlyList<string> Compact_Outboxes(ISupervisionPaths paths, IReadOnlyList<IOrchestrationSession> sessions)
    {
        List<string> failures = [];

        foreach (var session in sessions.Where(session => session.ClosedUtc == null && session.EndeavourId != null))
            Compact_Outbox(paths, session, failures);

        return failures;
    }

    static List<IOrchestrationSession> Open_Siblings_Of(IOrchestrationSession session, IReadOnlyList<IOrchestrationSession> openLinked)
    {
        if (session.ClosedUtc != null || session.EndeavourId == null)
            return [];

        return [.. openLinked.Where(other =>
            string.Equals(other.EndeavourId, session.EndeavourId, StringComparison.Ordinal)
            && !string.Equals(other.OrchId, session.OrchId, StringComparison.Ordinal))];
    }

    /// <summary>
    /// One read per open linked orchestration. A failed read is reported once, and every digest that would
    /// have listed it is left as it is this tick rather than rewritten with the sibling missing — a sibling
    /// silently vanishing from ENDEAVOUR.md would read as "it closed".
    /// </summary>
    static Dictionary<string, SiblingDigestInput?> Read_InputsOnce(ISupervisionPaths paths, IReadOnlyList<IOrchestrationSession> openLinked, List<string> failures)
    {
        Dictionary<string, SiblingDigestInput?> inputs = new(StringComparer.Ordinal);

        // Only what some digest will print: a linked orchestration whose siblings have all closed is listed
        // by nobody, so reading its ledger and channels would buy nothing.
        var listed = openLinked
            .GroupBy(session => session.EndeavourId, StringComparer.Ordinal)
            .Where(endeavour => endeavour.Count() > 1)
            .SelectMany(endeavour => endeavour);

        foreach (var session in listed)
        {
            try
            {
                inputs[session.OrchId] = EndeavourDigest_Reader.Read_Inputs(paths, [session])[0];
            }
            catch (Exception exception)
            {
                inputs[session.OrchId] = null;
                failures.Add($"could not read '{session.OrchId}' for its siblings' ENDEAVOUR.md — {Describe(exception)}");
            }
        }

        return inputs;
    }

    static void Reconcile_List(ISupervisionPaths paths, IOrchestrationSession session, IReadOnlyList<IOrchestrationSession> siblings, List<string> failures)
    {
        try
        {
            EndeavourMarkers_Sync.Sync(paths, session, siblings);
        }
        catch (Exception exception)
        {
            failures.Add($"could not reconcile '{paths.Get_SiblingsListFile(session.OrchId)}' — {Describe(exception)}");
        }
    }

    static void Reconcile_Digest(
        ISupervisionPaths paths,
        IOrchestrationSession session,
        IReadOnlyList<IOrchestrationSession> siblings,
        IReadOnlyDictionary<string, SiblingDigestInput?> inputs,
        List<string> failures)
    {
        var file = paths.Get_EndeavourDigestFile(session.OrchId);

        try
        {
            if (siblings.Count == 0)
            {
                DerivedFile_Writer.Delete_IfPresent(file);
                return;
            }

            List<SiblingDigestInput> read = [];

            foreach (var sibling in siblings)
            {
                // Already reported by Read_InputsOnce; the file keeps last tick's text until the read heals.
                if (inputs[sibling.OrchId] is not { } input)
                    return;

                read.Add(input);
            }

            DerivedFile_Writer.Write_IfChanged(file, EndeavourDigest_Builder.Build(read));
        }
        catch (Exception exception)
        {
            failures.Add($"could not reconcile '{file}' — {Describe(exception)}");
        }
    }

    static void Compact_Outbox(ISupervisionPaths paths, IOrchestrationSession session, List<string> failures)
    {
        var outbox = paths.Get_SiblingOutboxFile(session.OrchId);

        try
        {
            if (File.Exists(outbox))
                Channel_Compactor.Compact_IfNeeded(outbox);
        }
        catch (Exception exception)
        {
            // The compactor swallows its own failures ("not this pass"); this is the belt for the stat.
            failures.Add($"could not compact '{outbox}' — {Describe(exception)}");
        }
    }

    static string Describe(Exception exception)
    {
        return $"{exception.GetType().Name}: {exception.Message}";
    }
}
