using AIOrchestratorCoreLib.Configuration.SettingsWriting;

namespace AIOrchestratorCoreLib.Configuration.SettingsPresentation;

/// <summary>
/// THE LINE AN EDITOR SHOWS BESIDE A ROW AFTER AN EDIT, and whether it is a refusal (plan 04 Task 9). The WPF
/// window is UI-relaxed and untestable by this suite, so "what does the owner read after pressing Apply" is
/// decided here, where a test can pin it — and the words are the web page's own ("Saved.", "Reset.", "Not
/// applied (…)."), so the two desktop surfaces answer one edit alike.
///
/// <para>
/// A REFUSAL IS ALWAYS THE WRITER'S OWN MESSAGE (decision 21): the definition's words for a value it turned
/// down, the writer's words for a file it could not read (P33 — <c>WriteFailed</c>, which is drawn as a
/// refusal and never as "Saved."). This class adds words only where the writer gave none.
/// </para>
/// </summary>
public static class SettingWriteNote_Formatter
{
    public const string SAVED = "Saved.";

    public const string RESET = "Reset.";

    public static (string Text, bool IsRefusal) Describe(SettingsWriteOutcomes outcome, string? message_OrNull)
    {
        if (!Settings_Writer.Took_Effect(outcome))
            return (message_OrNull ?? $"Not applied ({outcome}).", true);

        // A Reset of a row that was never set carries the writer's "nothing to reset" sentence, which is truer
        // than a bare "Reset." — the owner pressed it expecting an effect, and there was none to have.
        if (outcome == SettingsWriteOutcomes.Reset)
            return (message_OrNull ?? RESET, false);

        return (SAVED, false);
    }

    /// <summary>
    /// THE WRITE THREW (the rename refused, the disk full — Task 2's rule that a write which did not happen throws
    /// rather than reports), or the token's own door refused a secrets.json it could not read (Task 2c, P35).
    /// Either way nothing was written, and the sentence says so before the system's own reason.
    /// </summary>
    public static string Describe_WriteThrew(string reason)
    {
        return $"Nothing was saved: {reason}";
    }
}
