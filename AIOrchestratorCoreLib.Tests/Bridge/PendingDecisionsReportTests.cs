using AIOrchestratorCoreLib.Bridge.Decisions;
using AIOrchestratorCoreLib.Bridge.EngineState;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// The text behind /pending — see <see cref="PendingDecisions_Report"/>. Every clock here is a
/// fixed <see cref="DateTime"/>, never <c>DateTime.UtcNow</c>, because the report's whole job is
/// printing ages and countdowns and a flaky clock would make the assertions about those
/// meaningless.
/// </summary>
public class PendingDecisionsReportTests
{
    static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AnEmptyList_GivesExactlyTheNothingPendingConstant()
    {
        var report = PendingDecisions_Report.Build([], [], Now);

        Assert.Equal(PendingDecisions_Report.NOTHING_PENDING, report);
    }

    /// <summary>
    /// OLDEST FIRST. What has been waiting longest is what the owner has most likely lost track
    /// of — burying it under newer questions is how a five-day-old ask never gets answered.
    /// </summary>
    [Fact]
    public void QuestionsAreListed_OldestFirst()
    {
        var older = new OpenQuestionRecord { OrchId = "old-orch", Text = "older question", AskedUtc = Now.AddDays(-3) };
        var newer = new OpenQuestionRecord { OrchId = "new-orch", Text = "newer question", AskedUtc = Now.AddMinutes(-5) };

        // Deliberately passed newest-first, so a correct sort is the only way the assertion holds.
        var report = PendingDecisions_Report.Build([newer, older], [], Now);

        Assert.True(report.IndexOf("old-orch", StringComparison.Ordinal) < report.IndexOf("new-orch", StringComparison.Ordinal));
    }

    [Fact]
    public void AQuestionWithNoDeadline_IsListedWithItsAge_AndNoCountdown()
    {
        var question = new OpenQuestionRecord { OrchId = "o1", Text = "merge now?", AskedUtc = Now.AddHours(-2), DeadlineUtc = null };

        var report = PendingDecisions_Report.Build([question], [], Now);

        Assert.Contains("asked 2h ago", report);
        Assert.DoesNotContain("if unanswered", report);
        Assert.DoesNotContain("DEADLINE", report);
    }

    /// <summary>
    /// The option number is 1-BASED, matching the numbered list the owner actually sees under the
    /// question on the phone. An off-by-one here is exactly the class of defect this stage exists
    /// to catch — the app would tell the owner one option is about to be taken while a different
    /// one fires.
    /// </summary>
    [Fact]
    public void AQuestionWithADeadlineAndADefault_NamesTheOneBasedOptionNumberThatWillBeTaken()
    {
        var question = new OpenQuestionRecord
        {
            OrchId = "o1",
            Text = "merge or hold?",
            AskedUtc = Now.AddMinutes(-10),
            DeadlineUtc = Now.AddHours(1),
            DefaultOptionIndex = 2,
        };

        var report = PendingDecisions_Report.Build([question], [], Now);

        Assert.Contains("option 3 taken in 1h if unanswered", report);
    }

    /// <summary>
    /// A high-risk question never gets a default, no matter what index sits in the record: it is
    /// denied on timeout, and the report must never say otherwise — defaulting a push or a deploy
    /// because a phone was in a pocket is the one outcome the whole feature exists to prevent.
    /// </summary>
    [Fact]
    public void AHighRiskQuestionWithADeadline_SaysItWillBeDenied_AndNeverNamesADefault()
    {
        var question = new OpenQuestionRecord
        {
            OrchId = "o1",
            Text = "push to production?",
            AskedUtc = Now.AddMinutes(-10),
            DeadlineUtc = Now.AddHours(1),
            DefaultOptionIndex = 0,
            IsHighRisk = true,
        };

        var report = PendingDecisions_Report.Build([question], [], Now);

        Assert.Contains("denied in 1h if unanswered", report);
        Assert.DoesNotContain("option", report);
        Assert.DoesNotContain("taken", report);
    }

    /// <summary>
    /// A deadline already in the past must render as PASSED, never as a negative countdown. This
    /// is exactly how "on task under a minute" bugs happen elsewhere in this app (decision 12 in
    /// CLAUDE.md): a negative duration formatted as if it were still counting down reads as a
    /// confident wrong number instead of the stale state it actually is.
    /// </summary>
    [Fact]
    public void ADeadlineAlreadyInThePast_RendersAsPassed_NotAsANegativeCountdown()
    {
        var question = new OpenQuestionRecord
        {
            OrchId = "o1",
            Text = "merge or hold?",
            AskedUtc = Now.AddHours(-5),
            DeadlineUtc = Now.AddMinutes(-1),
            DefaultOptionIndex = 0,
        };

        var report = PendingDecisions_Report.Build([question], [], Now);

        Assert.Contains("DEADLINE PASSED", report);
        Assert.DoesNotContain("-", report);
    }

    [Fact]
    public void APendingConfirmation_RendersWithItsRemainingWindow()
    {
        var confirmation = new PendingConfirmationRecord
        {
            Code = "1234",
            OrchId = "o1",
            OptionText = "Push",
            QuestionText = "push to production?",
            ExpiresUtc = Now.AddMinutes(7),
        };

        var report = PendingDecisions_Report.Build([], [confirmation], Now);

        Assert.Contains("type the code within 7m", report);
    }

    [Fact]
    public void AnExpiredConfirmation_SaysTheCodeHasExpired()
    {
        var confirmation = new PendingConfirmationRecord
        {
            Code = "1234",
            OrchId = "o1",
            OptionText = "Push",
            QuestionText = "push to production?",
            ExpiresUtc = Now.AddMinutes(-1),
        };

        var report = PendingDecisions_Report.Build([], [confirmation], Now);

        Assert.Contains("EXPIRED", report);
    }

    /// <summary>
    /// A five-item /pending must not become a screen the owner has to scroll — a long question is
    /// truncated to one line rather than dumping its full text (and its numbered option list) into
    /// the digest.
    /// </summary>
    [Fact]
    public void AVeryLongQuestionText_IsTruncatedToOneLine()
    {
        var longText = new string('x', 200);
        var question = new OpenQuestionRecord { OrchId = "o1", Text = longText, AskedUtc = Now.AddMinutes(-1) };

        var report = PendingDecisions_Report.Build([question], [], Now);

        Assert.DoesNotContain(longText, report);
        Assert.Contains("…", report);
        Assert.Single(report.Split('\n'));
    }

    /// <summary>
    /// THE CODE MUST NEVER LEAK OUTSIDE THE TELEGRAM MESSAGE IT IS SHOWN IN. A read-back code is a
    /// defence against a tap taken by an unlocked phone in someone else's hand; if the digest
    /// printed it, anyone who could read /pending could also answer the confirmation, defeating the
    /// whole second gesture.
    /// </summary>
    [Fact]
    public void TheReportNeverContainsAPendingConfirmationsCode()
    {
        var confirmation = new PendingConfirmationRecord
        {
            Code = "SECRET-CODE-99",
            OrchId = "o1",
            OptionText = "Push",
            QuestionText = "push to production?",
            ExpiresUtc = Now.AddMinutes(5),
        };

        var report = PendingDecisions_Report.Build([], [confirmation], Now);

        Assert.DoesNotContain(confirmation.Code, report);
    }
}
