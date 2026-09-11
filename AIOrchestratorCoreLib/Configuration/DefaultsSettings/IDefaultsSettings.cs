namespace AIOrchestratorCoreLib.Configuration.DefaultsSettings;

/// <summary>
/// WHAT A REQUEST GETS WHEN IT DOES NOT SAY — the <c>defaults</c> block of config.json, the owner's
/// side of decisions the general supervisor otherwise makes on their behalf.
///
/// <para>
/// One setting so far, and it is the one the first live round asked for. The shape of an
/// orchestration started without an explicit <c>mode</c> has been BASIC since 2026-08-13 — the
/// owner's own directive, as a cost-saving measure — and it was a constant in the request reader, so
/// getting a crew meant saying "full" in the message every single time. The owner who set the rule is
/// the one who should be able to change it without a rebuild; the shape a request DOES name still
/// wins, in both directions, because a default is what settles a silence and not something that
/// overrules a sentence.
/// </para>
/// </summary>
public interface IDefaultsSettings
{
    /// <summary>
    /// The shape a <c>start-orchestration</c> without a <c>mode</c> starts in: true for basic (one
    /// solo, no supervisor), false for a full crew. Never null — an absent or unreadable
    /// <c>defaults</c> block means the shipped default, which is basic.
    /// </summary>
    bool OrchestrationIsBasic { get; }
}
