using AIOrchestratorCoreLib.Status.SessionModelReading;

namespace AIOrchestratorCoreLib.Formatting;

/// <summary>
/// The ONE wording for a model-and-effort reading, so the pulse's lead line, its member rows and
/// anything that quotes the dial later all say the same thing about the same session. Item 12 of
/// the project decisions: never a second copy of a formatter.
/// </summary>
public static class ModelReading_Formatter
{
    /// <summary>
    /// "Fable 5.1 xhigh" — the model as Claude Code names it, then the dial, a single space between.
    /// No separator glyph inside the field: it is appended to rows that already use ` · ` BETWEEN
    /// fields, and a dot inside one would make the pair read as two.
    ///
    /// An unknown effort leaves JUST the model, with nothing trailing. An older Claude Code and a
    /// model without the dial both land here, and a placeholder would read as a reading that failed
    /// rather than a dial that does not exist.
    /// </summary>
    public static string Describe(ISessionModelReading reading)
    {
        if (reading.EffortLevel == null)
            return reading.ModelDisplayName;

        return $"{reading.ModelDisplayName} {reading.EffortLevel}";
    }

    /// <summary>
    /// The same, or null when there is no reading — for the surfaces that DROP the field rather
    /// than print an empty one. A dangling separator reads as a value that failed to load, when the
    /// truth is that the session has not reported one.
    /// </summary>
    public static string? Describe_OrNull(ISessionModelReading? reading)
    {
        if (reading == null)
            return null;

        return Describe(reading);
    }
}
