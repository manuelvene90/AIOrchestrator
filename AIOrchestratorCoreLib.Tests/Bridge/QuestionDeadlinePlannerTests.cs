using AIOrchestratorCoreLib.Bridge.Decisions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

public class QuestionDeadlinePlannerTests
{
    static readonly DateTime AskedUtc = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
    static readonly DateTime DeadlineUtc = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc); // 2h window

    /// <summary>
    /// No deadline is the pre-existing behaviour — a question waits until it is answered — and this
    /// must be un-inventable: nothing in Decide is allowed to conjure a deadline out of the clock
    /// alone, whatever nowUtc happens to be, including a nowUtc far in the future.
    /// </summary>
    [Theory]
    [InlineData("2026-09-01T10:00:00Z")]
    [InlineData("2026-09-01T12:00:00Z")]
    [InlineData("2099-01-01T00:00:00Z")]
    public void NoDeadline_IsAlwaysNone_WhateverTheClockSays(string nowUtcText)
    {
        var nowUtc = DateTime.Parse(nowUtcText, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);

        var action = QuestionDeadline_Planner.Decide(
            AskedUtc, deadlineUtc: null, reminderAlreadySent: false, isHighRisk: false, defaultOptionIndex: null, nowUtc);

        Assert.Equal(QuestionDeadlineActions.None, action);
    }

    [Fact]
    public void BeforeTheHalfwayPoint_IsNone()
    {
        var nowUtc = QuestionDeadline_Planner.Compute_ReminderAtUtc(AskedUtc, DeadlineUtc) - TimeSpan.FromMinutes(1);

        var action = QuestionDeadline_Planner.Decide(
            AskedUtc, DeadlineUtc, reminderAlreadySent: false, isHighRisk: false, defaultOptionIndex: null, nowUtc);

        Assert.Equal(QuestionDeadlineActions.None, action);
    }

    /// <summary>
    /// AT the halfway instant exactly the reminder must already fire — a gate that is never due at
    /// its own instant is silently longer than it advertises, one tick at a time, everywhere it is
    /// used. Both this and the deadline check below assert the boundary with '>=' explicitly.
    /// </summary>
    [Fact]
    public void AtTheHalfwayInstantExactly_IsRemind()
    {
        var reminderAtUtc = QuestionDeadline_Planner.Compute_ReminderAtUtc(AskedUtc, DeadlineUtc);

        var action = QuestionDeadline_Planner.Decide(
            AskedUtc, DeadlineUtc, reminderAlreadySent: false, isHighRisk: false, defaultOptionIndex: null, nowUtc: reminderAtUtc);

        Assert.Equal(QuestionDeadlineActions.Remind, action);
    }

    [Fact]
    public void PastTheHalfwayPoint_IsRemind()
    {
        var reminderAtUtc = QuestionDeadline_Planner.Compute_ReminderAtUtc(AskedUtc, DeadlineUtc);
        var nowUtc = reminderAtUtc + TimeSpan.FromMinutes(1);

        var action = QuestionDeadline_Planner.Decide(
            AskedUtc, DeadlineUtc, reminderAlreadySent: false, isHighRisk: false, defaultOptionIndex: null, nowUtc);

        Assert.Equal(QuestionDeadlineActions.Remind, action);
    }

    /// <summary>
    /// Once the reminder has been sent, it must not fire again before the deadline — a reminder is an
    /// edit that happens exactly once, never a second message and never a repeated edit either, so
    /// this must go back to None rather than re-triggering Remind on every later tick.
    /// </summary>
    [Fact]
    public void OnceReminderAlreadySent_IsNone_UntilTheDeadline_NeverAWaterfall()
    {
        var justBeforeDeadline = DeadlineUtc - TimeSpan.FromMinutes(1);

        var action = QuestionDeadline_Planner.Decide(
            AskedUtc, DeadlineUtc, reminderAlreadySent: true, isHighRisk: false, defaultOptionIndex: null, nowUtc: justBeforeDeadline);

        Assert.Equal(QuestionDeadlineActions.None, action);
    }

    /// <summary>
    /// AT the deadline instant exactly it must already act, mirroring the halfway boundary above —
    /// asserted with both '>=' explicitly so a future edit cannot quietly change one comparison to
    /// '>' and leave the deadline one tick longer than it claims.
    /// </summary>
    [Fact]
    public void AtTheDeadlineInstantExactly_WithNoDefault_IsExpireAsDeny()
    {
        var action = QuestionDeadline_Planner.Decide(
            AskedUtc, DeadlineUtc, reminderAlreadySent: true, isHighRisk: false, defaultOptionIndex: null, nowUtc: DeadlineUtc);

        Assert.Equal(QuestionDeadlineActions.ExpireAsDeny, action);
    }

    [Fact]
    public void AtTheDeadlineInstantExactly_WithADefault_IsApplyDefault()
    {
        var action = QuestionDeadline_Planner.Decide(
            AskedUtc, DeadlineUtc, reminderAlreadySent: true, isHighRisk: false, defaultOptionIndex: 1, nowUtc: DeadlineUtc);

        Assert.Equal(QuestionDeadlineActions.ApplyDefault, action);
    }

    [Fact]
    public void PastTheDeadline_WithADefaultAndNotHighRisk_IsApplyDefault()
    {
        var nowUtc = DeadlineUtc + TimeSpan.FromMinutes(5);

        var action = QuestionDeadline_Planner.Decide(
            AskedUtc, DeadlineUtc, reminderAlreadySent: true, isHighRisk: false, defaultOptionIndex: 0, nowUtc);

        Assert.Equal(QuestionDeadlineActions.ApplyDefault, action);
    }

    [Fact]
    public void PastTheDeadline_WithNoDefault_IsExpireAsDeny()
    {
        var nowUtc = DeadlineUtc + TimeSpan.FromMinutes(5);

        var action = QuestionDeadline_Planner.Decide(
            AskedUtc, DeadlineUtc, reminderAlreadySent: true, isHighRisk: false, defaultOptionIndex: null, nowUtc);

        Assert.Equal(QuestionDeadlineActions.ExpireAsDeny, action);
    }

    /// <summary>
    /// THE LOAD-BEARING CASE. A high-risk question must expire as a deny even when a default option
    /// index is present — high risk does not merely lack a default, it is FORBIDDEN from ever using
    /// one. A push approved because a phone sat unattended in a pocket past the deadline is precisely
    /// the outcome the whole second-gesture mechanism (the confirmation code) exists to prevent; if
    /// this ever resolved to ApplyDefault instead, that protection would have a silent back door
    /// through the timeout path rather than the tap path.
    /// </summary>
    [Fact]
    public void PastTheDeadline_HighRiskWithADefaultPresent_StillExpiresAsDeny_NeverAppliesTheDefault()
    {
        var nowUtc = DeadlineUtc + TimeSpan.FromMinutes(5);

        var action = QuestionDeadline_Planner.Decide(
            AskedUtc, DeadlineUtc, reminderAlreadySent: true, isHighRisk: true, defaultOptionIndex: 0, nowUtc);

        Assert.Equal(QuestionDeadlineActions.ExpireAsDeny, action);
    }

    [Fact]
    public void ComputeReminderAtUtc_IsTheMidpoint()
    {
        var expectedMidpoint = AskedUtc + TimeSpan.FromHours(1);

        Assert.Equal(expectedMidpoint, QuestionDeadline_Planner.Compute_ReminderAtUtc(AskedUtc, DeadlineUtc));
    }

    /// <summary>
    /// An inverted window (a deadline stamped before the asking time — corrupt data, or a clock
    /// skew) must not produce a reminder time in the past-tense middle of nowhere; it collapses to
    /// askedUtc, and Decide separately expires it on the very next tick since nowUtc is then always
    /// past the deadline too.
    /// </summary>
    [Fact]
    public void ComputeReminderAtUtc_ReturnsAskedUtc_ForAnInvertedWindow()
    {
        var invertedDeadline = AskedUtc - TimeSpan.FromHours(1);

        Assert.Equal(AskedUtc, QuestionDeadline_Planner.Compute_ReminderAtUtc(AskedUtc, invertedDeadline));
    }
}
