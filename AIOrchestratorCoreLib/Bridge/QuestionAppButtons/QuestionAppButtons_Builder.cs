using AIOrchestratorCoreLib.Bridge.Decisions;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Formatting;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Bridge.QuestionAppButtons;

/// <summary>
/// THE APP'S OWN BUTTONS UNDER A QUESTION — "❔ Explain the options" and "💬 Let's talk" — as
/// <c>questions.appButtons</c> lists them (plan 04 task 11; owner 2026-09-24, entry [123]: <i>"Can we have a
/// setting that lets us decide what buttons we want under the questions? I'd add all 2 or just one of the
/// two."</i>). Every fact about an app button that differs between the two lives here, keyed on the word: its
/// label, the request the session receives, what the question message becomes on a tap, and the closure
/// reason the log remembers.
///
/// <para>
/// MOVED OUT OF <c>BridgeEngineModel</c> under the move-out rule (code-conventions): the engine used to build
/// the one Let's talk record inline in <c>Register_Buttons</c> and pick its rewrite and closure reason inline in
/// <c>Handle_CallbackTap_Async</c>. With two buttons each of those became a choice, and a choice written three
/// times in a 15 000-line file is three places for Explain and Let's talk to drift apart.
/// </para>
/// <para>
/// WHAT DOES NOT DIFFER STAYS SHARED WITH THE OPTIONS. Both buttons ride the question's own nonce at the
/// indices after its options, so they are consumed by the same single-use group, handled on the same tap path
/// (a CallbackToken payload, not an <c>opt-</c> special case) and routed to the session as an app-composed
/// owner message, exactly as Let's talk already was. A default option can never land on one: the deadline
/// sweep matches <c>DefaultOptionIndex</c>, which is always below the option count.
/// </para>
/// </summary>
public static class QuestionAppButtons_Builder
{
    /// <summary>
    /// One pending record and its label per word, in the setting's order. <paramref name="firstIndex"/> is the
    /// question's option count, so the payloads continue its sequence. Neither button is ever high risk: each
    /// asks for an explanation and takes no decision, and a code in front of the safe way out of a dangerous
    /// question would make it the hardest button to press.
    /// </summary>
    public static IReadOnlyList<(PendingButtonRecord Record, string Label)> Build(
        IReadOnlyList<string> appButtons,
        string nonce,
        int firstIndex,
        long? threadId,
        string questionText,
        long groupId,
        DateTime expiresUtc)
    {
        List<(PendingButtonRecord Record, string Label)> built = [];

        for (var offset = 0; offset < appButtons.Count; offset++)
        {
            var word = appButtons[offset];

            var record = new PendingButtonRecord
            {
                Data = CallbackToken.Build(nonce, firstIndex + offset),
                ThreadId = threadId,
                OptionText = Request_For(word),
                QuestionText = questionText,
                GroupId = groupId,
                ExpiresUtc = expiresUtc,
                IsHighRisk = false,
                AppButton = word,
            };

            built.Add((record, Label_For(word)));
        }

        return built;
    }

    /// <summary>
    /// The question message after a tap on <paramref name="tapped"/>: the acknowledgement of the app button that
    /// was tapped, or "✅ &lt;option&gt;" for an option. Both app buttons CLOSE the question and choose nothing —
    /// master's Explain closed it too (<c>a58ef7e</c>), and stamped its request as a ✅ choice, which this does not.
    /// </summary>
    public static string Build_Rewrite(PendingButtonRecord tapped)
    {
        return tapped.AppButton switch
        {
            null => QuestionPrompt_Builder.Build_AnsweredText(tapped.QuestionText, tapped.OptionText),
            QuestionAppButton_Names.EXPLAIN => QuestionPrompt_Builder.Build_ExplainText(tapped.QuestionText),

            // TALK, and any word a later build wrote that this one does not know: the serializer maps an
            // unknown word to talk, so this arm is the only reading an app button can reach besides Explain.
            _ => QuestionPrompt_Builder.Build_TalkText(tapped.QuestionText),
        };
    }

    /// <summary>What closed the question, in <see cref="QuestionClosure_Wording"/>'s fixed vocabulary.</summary>
    public static string Describe_Closure(PendingButtonRecord tapped)
    {
        return tapped.AppButton switch
        {
            null => QuestionClosure_Wording.TAPPED_OPTION,
            QuestionAppButton_Names.EXPLAIN => QuestionClosure_Wording.EXPLAIN_REQUEST,
            _ => QuestionClosure_Wording.TALK_REQUEST,
        };
    }

    /// <summary>
    /// A word the setting's validator refused cannot reach here — the resolver falls to the layer below — so an
    /// unknown word is a broken build, and it says which word (an invariant, not external data).
    /// </summary>
    static string Label_For(string word)
    {
        return word switch
        {
            QuestionAppButton_Names.EXPLAIN => OwnerPush_Policy.EXPLAIN_LABEL,
            QuestionAppButton_Names.TALK => OwnerPush_Policy.TALK_LABEL,
            _ => throw new InvalidOperationException(Describe_Unknown(word)),
        };
    }

    static string Request_For(string word)
    {
        return word switch
        {
            QuestionAppButton_Names.EXPLAIN => OwnerPush_Policy.EXPLAIN_REQUEST,
            QuestionAppButton_Names.TALK => OwnerPush_Policy.TALK_REQUEST,
            _ => throw new InvalidOperationException(Describe_Unknown(word)),
        };
    }

    static string Describe_Unknown(string word)
    {
        return $"Unhandled app button '{word}' — {nameof(SettingValidators)}.{nameof(SettingValidators.QUESTION_APP_BUTTONS)} " +
            $"accepts only {string.Join(", ", QuestionAppButton_Names.ALL)}, so a word it passed has no button here";
    }
}
