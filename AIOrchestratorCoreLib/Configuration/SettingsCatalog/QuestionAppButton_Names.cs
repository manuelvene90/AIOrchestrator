namespace AIOrchestratorCoreLib.Configuration.SettingsCatalog;

/// <summary>
/// THE TWO BUTTONS THE APP MAY ADD UNDER A QUESTION, as the words <c>questions.appButtons</c> lists them — the
/// words, not the buttons, in the same way <see cref="PulseField_Names"/> holds the words of <c>pulse.fields</c>.
/// The labels, the requests a tap sends and the acknowledgements live beside each other in
/// <c>Bridge.OwnerPush_Policy</c>; the engine turns a word into a button in
/// <c>Bridge.QuestionAppButtons.QuestionAppButtons_Builder</c>.
///
/// <para>
/// WHY THERE ARE TWO AGAIN (owner, 2026-09-24, ai-orchestrator-29 entry [123]): <i>"my brother has transformed my
/// button under questions 'Explain in more details' into 'Let's talk' but I more often use the explain in more
/// details feature. Can we have a setting that lets us decide what buttons we want under the questions? I'd add
/// all 2 or just one of the two."</i> Master (<c>a58ef7e</c>) added <see cref="EXPLAIN"/> to every question; the
/// fork replaced it with <see cref="TALK"/> on 2026-09-09, reasoning that once both closed their question they
/// were synonyms. They are not synonyms to the owner: one asks for the options explained and a fresh question,
/// the other opens a conversation — so which ones appear is the owner's choice, not the app's.
/// </para>
/// </summary>
public static class QuestionAppButton_Names
{
    /// <summary>Master's "❔ Explain the options": the session explains each option, recommends one, and re-asks.</summary>
    public const string EXPLAIN = "explain";

    /// <summary>The fork's "💬 Let's talk": the session explains in prose, answers what the owner asks next, then re-asks.</summary>
    public const string TALK = "talk";

    /// <summary>Every legal word, in the order a picker offers them and the validator's error text names them.</summary>
    public static readonly IReadOnlyList<string> ALL = [EXPLAIN, TALK];
}
