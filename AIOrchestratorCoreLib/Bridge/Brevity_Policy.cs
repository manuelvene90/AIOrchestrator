using AIOrchestratorCoreLib.Channels;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// Measures how long a message to the owner actually was.
///
/// WHY THIS EXISTS: "max ~5 short lines" has been in the supervisor's role command from the start
/// and the owner still reports it as too verbose. Every other rule in this system that survived
/// contact with reality got a feedback loop rather than firmer wording — the task ledger, the
/// malformed headers — because a rule nobody measures is a rule that drifts. So the app counts the
/// message it just mirrored and tells the supervisor when it went over, with the real numbers.
///
/// It never truncates: cutting a message could remove the one line that mattered, and the owner
/// would have no way to know something was taken. Feedback only.
/// </summary>
public static class Brevity_Policy
{
    /// <summary>What a message to the owner should normally be.</summary>
    public const int TARGET_LINES = 3;

    /// <summary>Hard ceiling. Past this the supervisor gets told, with the count.</summary>
    public const int MAX_LINES = 5;

    /// <summary>A single line can also be a wall — 5 lines of 400 chars is not brevity.</summary>
    public const int MAX_CHARACTERS = 600;

    /// <summary>Never nag more than once per this window, however chatty the supervisor gets.</summary>
    public const int NUDGE_COOLDOWN_MINUTES = 20;

    /// <summary>
    /// THE LINES THAT COUNT — prose only, since the owner's ruling of 2026-09-10. A marker line is
    /// structure: the app turns `OPTION:` into a button and `STATE:` into a PULSE field, and neither
    /// is a sentence the owner reads.
    ///
    /// <para>
    /// WHY IT CHANGED, MEASURED. A well-formed question is SIX marker lines by construction — ROW,
    /// RISK, QUESTION, two OPTIONs, RECOMMEND — so a counter that included them coached EVERY question
    /// as over a five-line ceiling. Brief C asks for "zero coaching entries about format"; this was
    /// the surface generating them, about entries that were correct.
    /// </para>
    /// <para>
    /// ONE RULE, READ FROM THE GRAMMAR, so this counter and the tool that refuses before the write
    /// agree by construction. They did not: the tool counted prose and this counted everything, which
    /// is the same class of split E3 exists to end, one layer up from the marker words themselves.
    /// </para>
    /// </summary>
    public static int Count_Lines(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var lines = 0;

        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line) || ChannelGrammar.Is_MarkerLine(line))
                continue;

            lines++;
        }

        return lines;
    }

    public static bool Is_TooLong(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        return Count_Lines(text) > MAX_LINES || text.Length > MAX_CHARACTERS;
    }

    /// <summary>
    /// The feedback itself — it must carry the NUMBERS, or it is just the rule again.
    ///
    /// <para>
    /// <paramref name="deliveredMessages"/> is how many Telegram messages the entry actually became.
    /// It is stated first, and it is not decoration: on 2026-09-07 this note was read — by a
    /// supervisor and then by the owner it reported to — as the bridge having REFUSED the message,
    /// and the supervisor stopped re-sending an answer the owner had in fact received. Nothing here
    /// ever withholds a message; saying so removes the only reading under which it could.
    /// </para>
    /// </summary>
    public static string Build_NudgeBody(string text, int deliveredMessages = 1)
    {
        var delivery = deliveredMessages > 1
            ? $"It WAS delivered, split across {deliveredMessages} phone messages — nothing was dropped. "
            : "It WAS delivered in full — nothing was dropped. ";

        return delivery
            + $"That message was {Count_Lines(text)} lines / {text.Length} characters. The cap is {MAX_LINES} lines "
            + $"({TARGET_LINES} is the norm) and {MAX_CHARACTERS} characters, because it lands on a PHONE.\n\n"
            + "Cut it the way a busy person would want it: lead with the decision or the question, drop the reasoning "
            + "unless they asked for it, drop anything restating what they already know, and let the detail live in "
            + "the implementer channels and the app where they can go and get it. If it genuinely cannot be said in "
            + $"{MAX_LINES} lines, send the short version and offer the rest — they will ask.";
    }
}
