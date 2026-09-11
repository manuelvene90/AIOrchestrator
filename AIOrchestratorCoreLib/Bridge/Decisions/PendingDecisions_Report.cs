using System.Globalization;
using AIOrchestratorCoreLib.Bridge.EngineState;

namespace AIOrchestratorCoreLib.Bridge.Decisions;

/// <summary>
/// The text behind /pending: every decision waiting on the owner, with its AGE and what happens if
/// they keep not answering.
///
/// <para>
/// ANSWERED BY THE APP, NOT BY AN AGENT. /pending used to be routed to the general supervisor as an
/// instruction in English — "list every pending question that awaits me" — which meant the answer
/// cost a model turn, arrived when that session next ran, and was assembled from channel files by
/// something that could be mid-turn on something else. The app is holding the list in a field. The
/// same argument /progress already won.
/// </para>
/// <para>
/// AGE FIRST, DEADLINE SECOND, and both are needed: age is what tells the owner they have forgotten
/// something, the deadline is what tells them how long they have. A list of questions with neither
/// is the screenful they reported as unusable after a flight.
/// </para>
/// </summary>
public static class PendingDecisions_Report
{
    public const string NOTHING_PENDING = "Nothing is waiting on you.";

    public static string Build(
        IReadOnlyList<OpenQuestionRecord> openQuestions,
        IReadOnlyList<PendingConfirmationRecord> pendingConfirmations,
        DateTime nowUtc)
    {
        List<string> lines = [];

        // Oldest first: what has been waiting longest is what they are most likely to have lost.
        foreach (var question in openQuestions.OrderBy(question => question.AskedUtc))
            lines.Add(Describe_Question(question, nowUtc));

        foreach (var confirmation in pendingConfirmations.OrderBy(confirmation => confirmation.ExpiresUtc))
            lines.Add(Describe_Confirmation(confirmation, nowUtc));

        if (lines.Count == 0)
            return NOTHING_PENDING;

        return string.Join('\n', lines);
    }

    static string Describe_Question(OpenQuestionRecord question, DateTime nowUtc)
    {
        var risk = question.IsHighRisk ? "🔐 " : "";
        var age = Describe_Age(nowUtc - question.AskedUtc);
        var outcome = Describe_Outcome(question, nowUtc);

        return $"{risk}[{question.OrchId}] {First_Line(question.Text)} — asked {age} ago{outcome}";
    }

    static string Describe_Confirmation(PendingConfirmationRecord confirmation, DateTime nowUtc)
    {
        var remaining = confirmation.ExpiresUtc - nowUtc;

        // "tap again" was wrong for the same reason the expiry reply was: showing the code edits the
        // question message, and that edit takes the keyboard down with it. There is nothing left to
        // tap by the time this line can be printed.
        var window = remaining <= TimeSpan.Zero
            ? "the code has EXPIRED — type your answer, or ask again for fresh options"
            : $"type the code within {Describe_Age(remaining)}";

        return $"🔐 [{confirmation.OrchId}] {First_Line(confirmation.QuestionText)} → '{First_Line(confirmation.OptionText)}' — {window}";
    }

    static string Describe_Outcome(OpenQuestionRecord question, DateTime nowUtc)
    {
        if (question.DeadlineUtc == null)
            return "";

        var remaining = question.DeadlineUtc.Value - nowUtc;

        // A deadline in the past that is still listed means the sweep has not reached it yet — say
        // that, rather than printing a negative duration as if it were a countdown.
        if (remaining <= TimeSpan.Zero)
            return question.IsHighRisk || question.DefaultOptionIndex == null
                ? ", DEADLINE PASSED — being denied (timeout)"
                : ", DEADLINE PASSED — the default is being applied";

        if (question.IsHighRisk || question.DefaultOptionIndex == null)
            return $", denied in {Describe_Age(remaining)} if unanswered";

        // The owner counts options from 1, as the numbered list under the question does.
        var optionNumber = (question.DefaultOptionIndex.Value + 1).ToString(CultureInfo.InvariantCulture);
        return $", option {optionNumber} taken in {Describe_Age(remaining)} if unanswered";
    }

    /// <summary>
    /// Coarse on purpose: "3h" is what the owner acts on, "3h 12m 40s" is noise on a phone. A span
    /// under a minute reads as "under a minute" rather than "0m", which looks like a bug.
    /// </summary>
    static string Describe_Age(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        if (span.TotalMinutes < 1)
            return "under a minute";

        if (span.TotalHours < 1)
            return $"{(int)span.TotalMinutes}m";

        if (span.TotalDays < 1)
            return $"{(int)span.TotalHours}h";

        return $"{(int)span.TotalDays}d";
    }

    /// <summary>
    /// One line per decision. A question's text carries its numbered option list, and pasting all of
    /// it here turns a five-item /pending into a screen the owner has to scroll — which is the exact
    /// complaint that produced this command.
    /// </summary>
    static string First_Line(string text)
    {
        const int MAXIMUM_LENGTH = 90;

        var line = text.Split('\n')[0].Trim();

        return line.Length <= MAXIMUM_LENGTH ? line : line[..(MAXIMUM_LENGTH - 1)] + "…";
    }
}
