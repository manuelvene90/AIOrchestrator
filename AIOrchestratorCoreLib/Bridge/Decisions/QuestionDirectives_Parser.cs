using System.Globalization;
using AIOrchestratorCoreLib.Channels;

namespace AIOrchestratorCoreLib.Bridge.Decisions;

/// <summary>
/// Reads the two things a question may declare about what happens if the owner never answers:
/// <c>DEADLINE: 2h</c> and <c>DEFAULT: 2</c>.
///
/// <para>
/// WHY MARKERS AND NOT A JSON BLOCK. The entry format already carries <c>QUESTION:</c>,
/// <c>OPTION:</c> and <c>IMAGE:</c> lines, extracted the same way by the same engine method, and
/// they are written by an agent into a file a human also reads. Two more markers cost the kit one
/// paragraph; a fenced JSON block is a second format in the same file, and the format the entry
/// already has is the one every role command already knows.
/// </para>
/// <para>
/// BOTH ARE OPTIONAL AND INDEPENDENT. A deadline with no default is meaningful — the question
/// EXPIRES and says so — while a default with no deadline is not, because nothing would ever apply
/// it; that combination is read as no directive at all rather than as an invented deadline. Silence
/// from the agent means the old behaviour exactly: a question that waits indefinitely.
/// </para>
/// <para>
/// AGENT-WRITTEN, THEREFORE UNTRUSTED (decision 12). Every value here is a guess the app must not
/// crash on: an unparseable duration, a default index pointing past the end of the option list, a
/// negative one. Each is dropped individually, and dropping a DEFAULT never drops the deadline it
/// came with — losing the deadline would silently restore "waits for ever" for the one question
/// somebody bothered to bound.
/// </para>
/// </summary>
public static class QuestionDirectives_Parser
{
    // FROM THE GRAMMAR, and `static readonly` rather than `const` because of it: the grammar is a
    // FILE both this app and the bash tool read, so its values arrive at runtime. A `const` would
    // have to be a literal here, which is the ninth copy E3 removes.
    public static readonly string DEADLINE_MARKER = ChannelGrammar.Bare(ChannelGrammar.DEADLINE);
    public static readonly string DEFAULT_MARKER = ChannelGrammar.Bare(ChannelGrammar.DEFAULT);

    /// <summary>
    /// A deadline further out than this is treated as absent. An agent writing "9999h" has not
    /// declared a deadline, it has declared a number, and carrying it would put a question in
    /// /pending with an expiry nobody will ever see.
    /// </summary>
    public const int MAXIMUM_DEADLINE_HOURS = 168;

    /// <summary>
    /// Interprets the raw marker values. <paramref name="deadlineValues"/> and
    /// <paramref name="defaultValues"/> are what the engine's marker extraction returned — a list,
    /// because an agent can write the line twice; the FIRST readable one wins, so a repeated marker
    /// cannot silently override the one a human would read at the top.
    /// </summary>
    public static (TimeSpan? Deadline, int? DefaultOptionIndex) Parse(
        IReadOnlyList<string> deadlineValues,
        IReadOnlyList<string> defaultValues,
        int optionCount)
    {
        TimeSpan? deadline = null;

        foreach (var value in deadlineValues)
        {
            deadline = Parse_Duration_OrNull(value);

            if (deadline != null)
                break;
        }

        int? defaultOptionIndex = null;

        foreach (var value in defaultValues)
        {
            defaultOptionIndex = Parse_OptionIndex_OrNull(value, optionCount);

            if (defaultOptionIndex != null)
                break;
        }

        // A default nobody would ever apply is not a default. Kept as a separate statement rather
        // than folded into the loop so the asymmetry is visible: the deadline survives a bad
        // default, the default does not survive a missing deadline.
        if (deadline == null)
            defaultOptionIndex = null;

        return (deadline, defaultOptionIndex);
    }

    /// <summary>
    /// "2h", "90m", "45" (minutes when bare). Minutes are the bare unit because that is the scale
    /// the rest of this app states its own timings in.
    /// </summary>
    public static TimeSpan? Parse_Duration_OrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim().ToLowerInvariant();
        var multiplier = 1.0;

        if (text.EndsWith('h'))
        {
            multiplier = 60;
            text = text[..^1].Trim();
        }
        else if (text.EndsWith('m'))
        {
            text = text[..^1].Trim();
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
            return null;

        var minutes = amount * multiplier;

        // A zero or negative deadline would expire the question in the same tick that asked it,
        // which is a question the owner never gets to see.
        if (minutes <= 0 || minutes > MAXIMUM_DEADLINE_HOURS * 60)
            return null;

        return TimeSpan.FromMinutes(minutes);
    }

    /// <summary>
    /// The option NUMBER as the owner sees it — 1-based, matching the numbered list the buttons
    /// point at — returned as a 0-based index. Off-by-one here would silently default to the option
    /// next to the intended one, which is the failure this whole stage is about.
    /// </summary>
    public static int? Parse_OptionIndex_OrNull(string? value, int optionCount)
    {
        if (string.IsNullOrWhiteSpace(value) || optionCount <= 0)
            return null;

        if (!int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            return null;

        if (number < 1 || number > optionCount)
            return null;

        return number - 1;
    }
}
