namespace AIOrchestratorCoreLib.GeneralSupervision.StartOrchestrationRequest;

/// <summary>
/// A request dropped by the GENERAL supervisor (as a .json file under .requests/) asking the app
/// to start a new orchestration. The app is the executor; the general supervisor is the brain.
/// The orchestration id is ALLOCATED BY THE APP (repo-slug-n, incremental) — never requested.
/// </summary>
public interface IStartOrchestrationRequest
{
    /// <summary>The repo the general supervisor resolved (it maps the owner's colloquial phrasing first).</summary>
    string RepoQuery { get; }

    /// <summary>
    /// TRUE for basic (one solo session, no supervisor), FALSE for a full crew, and NULL when the
    /// request did not say — in which case <c>config.json</c>'s <c>defaults.orchestrationMode</c>
    /// decides, and the shipped value of that is basic.
    ///
    /// <para>
    /// THE NULL IS THE POINT, and it used to be collapsed here. "Absent" and "explicitly basic" were
    /// one value, so the owner's own directive of 2026-08-13 was a constant in the reader and getting
    /// a crew meant saying "full" in the message every single time. A default only means something if
    /// the silence it settles is still legible when it reaches the thing that holds the default.
    /// </para>
    /// <para>
    /// An UNRECOGNISED word is neither: the request file is rejected outright, because a typo must
    /// never decide the shape quietly in either direction.
    /// </para>
    /// </summary>
    bool? IsBasic { get; }

    /// <summary>
    /// THE OWNER'S OWN WORDS, carried with the request so the orchestration is born with something to
    /// do. Optional and usually present: the concierge writes the file the moment the owner says what
    /// they want, and dropping the "what" on the floor is how a full crew came up on 2026-09-06 with a
    /// supervisor, an implementer and a reviewer and nothing to work on.
    ///
    /// <para>
    /// IT IS NOT THE GENERAL SUPERVISOR'S TO WRITE, and this field is what keeps that true. The new
    /// orchestration's owner-channel.md stays READ-ONLY to it, as its protocol says; the APP appends
    /// the task there as a <c>FROM owner</c> entry, which is the same thing it already does with every
    /// message the owner types into a topic. One writer, one attribution, no new licence.
    /// </para>
    /// </summary>
    string? Task { get; }

    /// <summary>The request file, deleted after processing (success or failure) so it never loops.</summary>
    string SourceFilePath { get; }
}
