using AIOrchestratorCoreLib.GeneralSupervision.ParkedCloseRequest;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.GeneralSupervision;

/// <summary>
/// EVERY WORD THE OWNER READS AT THE MOMENT THEY TAP, for a promotion.
///
/// rev-5 F1: the prompt body was already kind-aware and rendered "Turn 'X' into a full crew?", while
/// the BUTTONS under it were hard-coded "✅ Close it" / "✋ Keep it open" and the post-tap edit said
/// "Closed — you confirmed" whatever had happened.
///
/// That is not a cosmetic mismatch. Reading those buttons, the safe-looking tap is "Keep it open" —
/// which records a DECLINED promotion, tells the solo the owner refused and not to ask again, and
/// leaves nobody aware that the question asked was never the question answered. On the confirming
/// side, the topic is left permanently saying an orchestration that just gained two sessions was
/// closed.
///
/// The same class already had a fix in this file's `finally` — the archive label follows the kind,
/// because a wrong one "would leave an audit trail saying the opposite of what happened". That
/// argument applies with far more force to the sentence the owner reads than to a filename, and it
/// was applied to the filename first.
/// </summary>
public class PromotionPromptWordingTests
{
    /// <summary>
    /// NOTHING THAT NAMES THE ACTION SAYS "CLOSE" — the buttons, the post-tap text, and the phrase
    /// used mid-sentence when the outcome is reported back.
    ///
    /// SCOPED TO THOSE THREE ON PURPOSE, and the first version of this test was not: it swept the
    /// prompt BODY too and went red, because the body deliberately says there is no going back to one
    /// session "without closing the orchestration". That sentence is the one-way warning and it is
    /// worth keeping — a hypothetical future close, explained, is not the same object as a BUTTON
    /// that says the app is closing something now. The property is about what labels the action.
    /// </summary>
    [Fact]
    public void NothingThatNamesThePromotionActionSaysClose()
    {
        var request = ParkedCloseRequest_Factory.Create_ForPromotion("crm-4", "the solo", "three subsystems now", "p.json");

        var (confirm, decline) = CloseConfirmationPrompt_Builder.Build_ButtonLabels(ParkedCloseKinds.Promotion);

        string[] everythingThatNamesTheAction =
        [
            confirm,
            decline,
            CloseConfirmationPrompt_Builder.Build_DecidedText(ParkedCloseKinds.Promotion, "crm-4", confirmed: true),
            CloseConfirmationPrompt_Builder.Build_DecidedText(ParkedCloseKinds.Promotion, "crm-4", confirmed: false),
            CloseConfirmationPrompt_Builder.Describe_Subject(request),
        ];

        foreach (var text in everythingThatNamesTheAction)
            Assert.DoesNotContain("clos", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// And the BODY never offers to close anything: it may explain that closing is the way back, but
    /// it must never carry the close prompt's headline, which is the sentence the owner reads first.
    /// </summary>
    [Fact]
    public void ThePromotionBodyNeverAsksToCloseTheOrchestration()
    {
        var body = CloseConfirmationPrompt_Builder.Build(
            ParkedCloseRequest_Factory.Create_ForPromotion("crm-4", "the solo", "three subsystems now", "p.json"),
            null);

        Assert.DoesNotContain("Close 'crm-4'", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Close orchestration", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("full crew", body);
    }

    /// <summary>
    /// And the buttons say what tapping them DOES, which is the half a "does not say close" assertion
    /// cannot check: a pair reading "Yes"/"No" would pass that and tell the owner nothing.
    /// </summary>
    [Fact]
    public void ThePromotionButtonsNameTheOutcome()
    {
        var (confirm, decline) = CloseConfirmationPrompt_Builder.Build_ButtonLabels(ParkedCloseKinds.Promotion);

        Assert.Contains("crew", confirm, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one session", decline, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// THE CLOSE WORDING IS UNCHANGED, so this fix cannot pass by making every kind neutral. Both
    /// close kinds keep the buttons and the decided text they had.
    /// </summary>
    [Theory]
    [InlineData(ParkedCloseKinds.Orchestration)]
    [InlineData(ParkedCloseKinds.Implementer)]
    public void ACloseStillReadsAsAClose(ParkedCloseKinds kind)
    {
        var (confirm, decline) = CloseConfirmationPrompt_Builder.Build_ButtonLabels(kind);

        Assert.Equal("✅ Close it", confirm);
        Assert.Equal("✋ Keep it open", decline);

        Assert.Contains("Closed — you confirmed", CloseConfirmationPrompt_Builder.Build_DecidedText(kind, "crm-4", confirmed: true));
        Assert.Contains("Kept open", CloseConfirmationPrompt_Builder.Build_DecidedText(kind, "crm-4", confirmed: false));
    }

    /// <summary>
    /// AN UNREADABLE REQUEST FALLS BACK TO NEUTRAL, never to the close wording. The decided text is
    /// written after a tap on a file that may since have been archived or corrupted, and guessing
    /// "closed" there is exactly how a record comes to say the opposite of what happened — which this
    /// path has already done once, at the archive label.
    /// </summary>
    [Fact]
    public void AnUnknownKindSaysNothingItCannotKnow()
    {
        foreach (var confirmed in new[] { true, false })
        {
            var text = CloseConfirmationPrompt_Builder.Build_DecidedText(null, "crm-4", confirmed);

            Assert.DoesNotContain("clos", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("crew", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("crm-4", text);
        }
    }
}
