using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// THE COLOUR A REPOSITORY'S TOPICS ARE CREATED WITH — brief F1; the rotation itself is
/// <see cref="TopicColor_Rotation"/>'s and the file format is <see cref="ConfigRepoColor_Writer"/>'s.
/// The engine calls this at its two <c>Create_ForumTopic_Async</c> sites, the only places that know
/// which repository a topic belongs to, and logs the warning it hands back.
///
/// <para>
/// A SETTING SINCE 2026-09-23 (plan 03 task 14, ruling R14). Owner: <i>"since we merged his forks the
/// topic icon gets colored without any context of why, red, blue, green, seemingly random."</i> The
/// rotation is deterministic, but nothing on the phone says "this repo = this colour", so it reads as
/// random. <c>topic.repoColours</c> ships true (today's behaviour, the fork author's) and classic states
/// false. OFF ASSIGNS NOTHING AND WRITES NOTHING: a colour picked "for later" would be the decision the
/// owner turned off, taken anyway — and it would surface the day someone flipped the setting back on,
/// as a colour nobody chose. A colour ALREADY on file is left there, so on-again restores it rather than
/// handing out the next in line. The caller resolves the flag from <c>_configProvider.Get_Current()</c>
/// at each creation, so a config.json edit is obeyed on the next topic without a restart.
/// </para>
/// <para>
/// ASSIGNED ON FIRST USE AND WRITTEN DOWN, because the rotation depends on what has already been handed
/// out and the repo list is reordered at runtime — a colour derived from a position would change under
/// the owner every time they dragged a row. A repository not in config.json at all (removed while an
/// orchestration on it is still open) gets no colour rather than a wrong one.
/// </para>
/// <para>
/// NEVER FAILS THE TOPIC. A colour is the least important thing happening on this path; every way of
/// not getting one ends in null, and the topic is created in Telegram's default. It never logs either:
/// the warning comes back as a value and the engine, which owns the log, writes it (code-conventions).
/// </para>
/// <para>
/// OUT OF <c>BridgeEngineModel.cs</c> (code-conventions: a piece the stage touches moves out), moved with
/// its two warning wordings verbatim.
/// </para>
/// </summary>
public static class RepoTopicColour_Resolver
{
    public static (int? Colour, string? WarningOrNull) Resolve(
        bool repoColoursOn,
        IReadOnlyList<IRepoEntry> repos,
        string repoName,
        ISupervisionPaths paths)
    {
        if (!repoColoursOn)
            return (null, null);

        try
        {
            var repo = repos.FirstOrDefault(entry => string.Equals(entry.Name, repoName, StringComparison.OrdinalIgnoreCase));

            if (repo == null)
                return (null, null);

            if (repo.TopicColor != null)
                return (repo.TopicColor, null);

            var inUse = repos.Where(entry => entry.TopicColor != null).Select(entry => entry.TopicColor!.Value).ToList();
            var colour = TopicColor_Rotation.Pick_ForNewRepo(inUse);

            // A colour that cannot be persisted is still USED for this topic — the alternative is a
            // repository whose topics are all Telegram's default while the file stays unwritable.
            // The next topic re-picks; the rotation is deterministic, so it very likely picks the
            // same one again.
            if (!ConfigRepoColor_Writer.Persist_Colour(paths, repo.Name, colour))
                return (colour, $"Topic colour for repo '{repo.Name}' could not be written to config.json — this topic uses it, the next one re-picks");

            return (colour, null);
        }
        catch (Exception ex)
        {
            // Broad by intent: this must never be the reason a topic is not created.
            return (null, $"Could not resolve a topic colour for repo '{repoName}' ({ex.Message}) — creating the topic in Telegram's default colour");
        }
    }
}
