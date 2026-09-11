using AIOrchestratorCoreLib.Running;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// F4, adversarial review 2026-09-09. The Telegram subject is what the owner reads on their phone,
/// and it said "resumes 03:00 UTC" for an appointment twenty-four hours away — a true instant,
/// formatted so that the reader takes it for this morning. CLAUDE.md decision 12: never a confident
/// wrong number. The delay is what a person actually needs, the absolute instant is what makes it
/// checkable, and the DATE joins the absolute half the moment the appointment is not today.
/// </summary>
public class LimitDeferralWordingTests
{
    static DateTime Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }

    [Fact]
    public void AnAppointmentLaterToday_IsTheDelayAndTheBareClock()
    {
        var said = LimitDeferral_Wording.Describe_Appointment(Utc(2026, 9, 9, 3, 0), Utc(2026, 9, 9, 0, 45));

        Assert.Equal("in 2 h 15 min (03:00 UTC)", said);
    }

    /// <summary>
    /// THE ONE THE PROBE CAUGHT. Same "03:00 UTC" as the row above, a whole day further out — and the
    /// old wording printed the two identically.
    /// </summary>
    [Fact]
    public void AnAppointmentOnAnotherDay_CARRIES_THE_DATE()
    {
        var said = LimitDeferral_Wording.Describe_Appointment(Utc(2026, 9, 10, 3, 0), Utc(2026, 9, 9, 0, 45));

        Assert.Equal("in 1 d 2 h (2026-09-10 03:00 UTC)", said);
        Assert.NotEqual(
            LimitDeferral_Wording.Describe_Appointment(Utc(2026, 9, 9, 3, 0), Utc(2026, 9, 9, 0, 45)),
            said);
    }

    [Fact]
    public void AnAppointmentJustAfterMidnight_CarriesTheDateEvenThoughItIsMinutesAway()
    {
        // "in 20 min" is the useful half here and "00:05 UTC" alone is the misleading one — a reader
        // at 23:45 has no way to tell whether that clock is behind or ahead of them.
        var said = LimitDeferral_Wording.Describe_Appointment(Utc(2026, 9, 10, 0, 5), Utc(2026, 9, 9, 23, 45));

        Assert.Equal("in 20 min (2026-09-10 00:05 UTC)", said);
    }

    [Fact]
    public void AnAppointmentAlreadyDue_SaysSo_RatherThanDressingItUpAsAWait()
    {
        Assert.Equal("now (03:00 UTC)", LimitDeferral_Wording.Describe_Appointment(Utc(2026, 9, 9, 3, 0), Utc(2026, 9, 9, 3, 0)));
        Assert.Equal("now (03:00 UTC)", LimitDeferral_Wording.Describe_Appointment(Utc(2026, 9, 9, 3, 0), Utc(2026, 9, 9, 3, 5)));
    }

    /// <summary>
    /// The whole point of the change, said as one assertion: no surface may print an appointment as a
    /// bare clock. A reader must never be able to take tomorrow for this morning.
    /// </summary>
    [Fact]
    public void NoAppointmentIsEverJustAClock()
    {
        var now = Utc(2026, 9, 9, 0, 45);

        foreach (var minutesOut in new[] { 1, 30, 200, 60 * 23, 60 * 25, 60 * 200 })
        {
            var said = LimitDeferral_Wording.Describe_Appointment(now.AddMinutes(minutesOut), now);

            Assert.StartsWith("in ", said);
            Assert.Contains(" UTC)", said);
        }
    }
}
