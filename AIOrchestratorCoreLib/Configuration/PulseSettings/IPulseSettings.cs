namespace AIOrchestratorCoreLib.Configuration.PulseSettings;

/// <summary>
/// THE STATUS LINE AND THE BARS HANGING OFF IT — every <c>pulse.*</c> row of the settings catalogue,
/// plus <c>general.buttons</c>, resolved catalogue → preset → config.json (plan 03). Read at the point
/// of effect from <c>_configProvider.Get_Current().Pulse</c>, never cached; the pure builders take these
/// values as parameters.
///
/// <para>
/// <c>general.buttons</c> IS HERE AND <c>pulse.holdToggle</c> IS NOT ON THE PHONE BLOCK, for the reason
/// <c>IPhoneSettings</c> states in full: a block is a JSON neighbourhood, not a catalogue category.
/// General's bar is the same kind of bar as <see cref="Buttons"/> — one list of verbs, one validator —
/// and the hold toggle decides a button on that bar.
/// </para>
/// </summary>
public interface IPulseSettings
{
    /// <summary>
    /// <c>pulse.fields</c> — the words of <c>PulseField_Names</c> the pulse line carries, in display
    /// order, each at most once. May be empty: a pulse with no fields is the owner's to choose.
    /// </summary>
    IReadOnlyList<string> Fields { get; }

    /// <summary><c>pulse.stepMinutes</c> — the step the "unchanged for" reading rounds to, and so how often the pulse is edited (1–60).</summary>
    int StepMinutes { get; }

    /// <summary>
    /// <c>pulse.buttons</c> — an orchestration topic's button bar, in display order. Each element's
    /// FIRST space-delimited token is a <c>BotCommandMenu</c> verb; what follows the space is its target
    /// ("tail sup"), because a tap carries no text of its own.
    /// </summary>
    IReadOnlyList<string> Buttons { get; }

    /// <summary><c>general.buttons</c> — the General topic's bar, same shape as <see cref="Buttons"/>. Empty means no bar.</summary>
    IReadOnlyList<string> GeneralButtons { get; }

    /// <summary>
    /// <c>pulse.holdToggle</c> — true draws the ⏸/▶ hold toggle on the PULSE bar, false on the receipt.
    /// Never both: this one value is the single fact that says which place.
    /// </summary>
    bool HoldToggle { get; }

    /// <summary>
    /// <c>pulse.unchangedFor</c> — whether the progress reading says how long the task count and percent
    /// have stood still ("unchanged 25 min", after 10 min, stepped), on whichever of <c>progress</c> and
    /// <c>merged</c> is drawn (plan 03 task 19, owner 2026-09-24 entry [100]). Read by the engine per topic
    /// per tick and handed to the builder.
    /// </summary>
    bool UnchangedFor { get; }
}
