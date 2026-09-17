using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Reviewing;

namespace AIOrchestratorCoreLib.Configuration.ReviewingSettings;

/// <summary>
/// The <c>reviewing</c> block, read:
///
/// <code>
/// "reviewing": { "reviewCapMinutes": 90, "holdCeilingMinutes": 180, "handledMemory": 200 }
/// </code>
///
/// <para>
/// READ AND NEVER WRITTEN — deliberately no <c>Write</c>, the contract <c>effort</c>,
/// <c>defaults</c> and the guardrail block all keep. No window has a field for these keys, so the
/// only thing a save could do is materialise THIS BUILD's answer into the owner's file as though
/// they had chosen it, freezing a default that is meant to move. That is not hypothetical: the
/// owner's config.json was found on 2026-09-12 pinned to a stale model for exactly that reason.
/// </para>
/// <para>
/// IT RESOLVES RATHER THAN PARSES, through <c>RerouteContract_Policy</c>'s own three resolvers — so
/// the four-layer precedence has one implementation, and the class whose doc calls itself "the dials
/// in one place" stays the place that knows their catalogue paths. <c>session: null</c> is implicit
/// there: none of the three is per-orchestration.
/// </para>
/// <para>
/// TOLERANT, because a config the app refuses to load is a bridge that does not start. A value out
/// of the catalogue row's bounds, or of the wrong type, is refused by the definition's own validator
/// and the resolver falls to the layer below, so a typo costs that one dial its default and never
/// the load.
/// </para>
/// </summary>
public static class ReviewingSettings_Json
{
    /// <summary>
    /// The block's own key in config.json, for callers that need to NAME it. DERIVED from the
    /// catalogue's own key rather than retyped, for decision 12's reason: nothing in this class uses
    /// it, because every lookup here goes through the catalogue PATH.
    /// </summary>
    public static readonly string REVIEWING_KEY = RerouteContract_Policy.REVIEWING_KEY;

    public static IReviewingSettings Parse(JsonObject? configRoot, JsonObject? presetTree)
    {
        return ReviewingSettings_Factory.Create(
            RerouteContract_Policy.Resolve_ReviewCap(presetTree, configRoot),
            RerouteContract_Policy.Resolve_HoldCeiling(presetTree, configRoot),
            RerouteContract_Policy.Resolve_HandledMemory(presetTree, configRoot));
    }
}
