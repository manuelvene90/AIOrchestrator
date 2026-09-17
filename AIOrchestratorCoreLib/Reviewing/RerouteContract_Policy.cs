using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Reviewing;

/// <summary>
/// THE DIALS AND THE WORDS OF A RE-REVIEW CONTRACT, in one place so no sweep spells them.
/// </summary>
public static class RerouteContract_Policy
{
    /// <summary>
    /// The config.json / preset node these dials live under: <c>{ "reviewing": { "reviewCapMinutes":
    /// 90, "holdCeilingMinutes": 180, "handledMemory": 200 } }</c> — grouped the way <c>printRunner</c>
    /// groups the dispatcher's own limits, because these three are what one sweep (the routed-report
    /// hold) does with its clock and its memory, not a thing a role spawns with.
    /// </summary>
    public const string REVIEWING_KEY = "reviewing";

    public const string REVIEW_CAP_MINUTES_KEY = "reviewCapMinutes";
    public const string HOLD_CEILING_MINUTES_KEY = "holdCeilingMinutes";

    /// <summary>
    /// <see cref="RerouteContract_Store.HANDLED_MEMORY"/>'s own catalogue key. Named here, beside its
    /// two siblings, rather than in <c>RerouteContract_Store</c> itself: this class is "the dials ...
    /// in one place" by its own class doc, and a caller reading the catalogue path for one reroute
    /// dial should find all three without also opening the store.
    /// </summary>
    public const string HANDLED_MEMORY_KEY = "handledMemory";

    /// <summary>
    /// HOW LONG A ROUTED CONTRACT HOLDS THE SUPERVISOR OUT OF THE LOOP. Past this the hold is
    /// released, the fix report wakes the supervisor on the ordinary digest, and one entry says the
    /// re-review never came back.
    ///
    /// <para>
    /// NINETY MINUTES IS A JUDGEMENT AND NOT A MEASUREMENT. A re-review is `quick` by construction —
    /// the delta is small, and the reviewer's own skill costs it at $1–4 — so this is generous by a
    /// wide margin. What matters is the DIRECTION of being wrong: expiring early costs one supervisor
    /// wake-up and restores exactly today's behaviour, while never expiring would leave a round
    /// silently unowned, which is the one failure this feature can produce that nobody would see.
    /// </para>
    /// <para>
    /// THE SHIPPED DEFAULT ONLY, NOW — decision 22's park closes here: the owner wants this dial
    /// configurable, the way <c>wake</c> and <c>bookkeeping</c> became (CLAUDE.md decision 12's
    /// entry). <see cref="Resolve_ReviewCap"/> is the resolved value a caller should read; this field
    /// is what "nothing configured" means and stays the SHIPPED DEFAULT the catalogue row reads from —
    /// never the other way round, or the catalogue and this constant could drift.
    /// </para>
    /// </summary>
    public static readonly TimeSpan REVIEW_CAP = TimeSpan.FromMinutes(90);

    /// <summary>
    /// THE ABSOLUTE CEILING ON A HOLD, and it exists because the cap alone is not one.
    ///
    /// <para>
    /// Expiring is meant to be one-shot in the sense <c>BudgetAlert_Planner</c> teaches: the contract
    /// is removed only once the entry saying "the re-review never came back" is actually on disk, so
    /// an alert refused by a meeting comes back on the tick after it ends. But that choke point also
    /// refuses PERMANENTLY when the owner is at the terminal
    /// (<c>OwnerPresence_Policy.Suppresses_SupervisorAttention</c>), and a refusal that never lifts
    /// would keep the contract, and therefore the hold, for ever — the supervisor kept out of a round
    /// nobody was ever told about, which is the ONE silent failure this whole feature can produce.
    /// </para>
    /// <para>
    /// So the retry is bounded rather than unbounded: past this the hold is released with a log line
    /// and no entry. Losing the alert costs the supervisor nothing it can act on — it is handed the
    /// fix report on its next digest, which is exactly what it got before this feature existed —
    /// whereas losing the release costs it the round.
    /// </para>
    /// <para>
    /// SAME SHIPPED-DEFAULT RULE AS <see cref="REVIEW_CAP"/>: this field is what an unconfigured
    /// machine runs with and what the catalogue row's default is READ from; <see cref="Resolve_HoldCeiling"/>
    /// is what a caller resolving the owner's actual choice should call.
    /// </para>
    /// </summary>
    public static readonly TimeSpan HOLD_CEILING = REVIEW_CAP + REVIEW_CAP;

    /// <summary>The single word that retracts a declaration: <c>REROUTE: cancel</c>.</summary>
    public const string CANCEL_WORD = "cancel";

    /// <summary>
    /// THE OWNER'S CONFIGURED REVIEW CAP, resolved config.json → preset → the shipped default
    /// (<see cref="REVIEW_CAP"/>) — the same three-layer order <see cref="Settings_Resolver"/> already
    /// gives every other Machine-scoped dial. <paramref name="presetTree"/> and
    /// <paramref name="configTree"/> are plain parsed trees (or null), exactly as
    /// <see cref="Settings_Resolver.Resolve"/> itself takes them, so this needs no loader and touches
    /// no disk.
    ///
    /// <para>
    /// NOT YET CALLED FROM <c>BridgeEngineModel</c> — that call site reads the field directly today and
    /// is out of this change's file set. This method is the resolution the owner asked for; wiring the
    /// one remaining read is a separate, reviewable step.
    /// </para>
    /// </summary>
    public static TimeSpan Resolve_ReviewCap(JsonObject? presetTree, JsonObject? configTree)
    {
        return Resolve_Minutes(REVIEW_CAP_MINUTES_KEY, presetTree, configTree, REVIEW_CAP);
    }

    /// <summary>The hold ceiling's equivalent of <see cref="Resolve_ReviewCap"/> — same layers, same caveat.</summary>
    public static TimeSpan Resolve_HoldCeiling(JsonObject? presetTree, JsonObject? configTree)
    {
        return Resolve_Minutes(HOLD_CEILING_MINUTES_KEY, presetTree, configTree, HOLD_CEILING);
    }

    /// <summary>
    /// <see cref="RerouteContract_Store.HANDLED_MEMORY"/>'s resolved value, kept here rather than on
    /// the store so all three dials resolve through the one method below. NOT YET CALLED from
    /// <c>RerouteContract_Store.Write_Set</c>, which is outside this change's file set and still reads
    /// the compiled constant directly — the same open wiring step <see cref="Resolve_ReviewCap"/> notes.
    /// </summary>
    public static int Resolve_HandledMemory(JsonObject? presetTree, JsonObject? configTree)
    {
        return Resolve_Int(HANDLED_MEMORY_KEY, presetTree, configTree, RerouteContract_Store.HANDLED_MEMORY);
    }

    static TimeSpan Resolve_Minutes(string key, JsonObject? presetTree, JsonObject? configTree, TimeSpan fallback)
    {
        return TimeSpan.FromMinutes(Resolve_Int(key, presetTree, configTree, (int)fallback.TotalMinutes));
    }

    /// <summary>
    /// THROUGH <see cref="Settings_Resolver.Resolve_Long"/>, THE ONE ACCESSOR — not a second copy of
    /// the same read. Writing this method first exposed a real defect in that accessor: it threw on
    /// the SHIPPED-DEFAULT layer of any <c>Kind = Int</c> row with a non-null default, which is every
    /// unconfigured machine's answer for these three dials. It was fixed where it lives rather than
    /// worked around here (CLAUDE.md decision 12) — this method is the int narrowing and the
    /// catalogue lookup, nothing else.
    ///
    /// <para>
    /// The fallback is only reached when the row resolves to JSON <c>null</c>, which none of the
    /// three can: all three are non-nullable with a shipped default. It is kept as the honest answer
    /// for a caller who adds a nullable reviewing dial later, and never as a way of swallowing a
    /// malformed value — a value of the wrong SHAPE still throws out of the resolver, loudly.
    /// </para>
    /// </summary>
    static int Resolve_Int(string key, JsonObject? presetTree, JsonObject? configTree, int fallback)
    {
        var definition = Catalog.Find_OrNull($"{REVIEWING_KEY}.{key}")
            ?? throw new InvalidOperationException($"No settings catalogue row for '{REVIEWING_KEY}.{key}'.");

        var resolved = Settings_Resolver.Resolve_Long(definition, presetTree, configTree, session: null);

        return resolved == null ? fallback : checked((int)resolved.Value);
    }
}
