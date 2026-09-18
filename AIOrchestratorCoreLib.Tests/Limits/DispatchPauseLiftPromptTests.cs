using AIOrchestratorCoreLib.Limits;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Limits;

public class DispatchPauseLiftPromptTests
{
    static readonly DateTime Now = new(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AnOffer_ExpiresAfterTwelveHours_NotBefore()
    {
        Assert.False(DispatchPauseLift_Prompt.Is_Expired(Now.AddHours(-11), Now));
        Assert.True(DispatchPauseLift_Prompt.Is_Expired(Now.AddHours(-13), Now));
    }

    [Fact]
    public void TheRequestOffer_NamesTheAsker_TheReason_TheResumeInstant_AndTheCommand()
    {
        var text = DispatchPauseLift_Prompt.Build_RequestOffer("general-supervisor", "the account was swapped", "at 2026-09-21 03:00 UTC (in 2 d 17 h)");

        Assert.Contains("general-supervisor", text);
        Assert.Contains("the account was swapped", text);
        Assert.Contains("2026-09-21 03:00 UTC", text);
        Assert.Contains("/resume_dispatch", text);
        Assert.Contains(DispatchPauseLift_Prompt.LIFT_LABEL, text);
    }

    [Fact]
    public void Lifted_SaysWhichReadingsStopCounting()
    {
        Assert.Contains("before 2026-09-18 12:00 UTC are ignored", DispatchPauseLift_Prompt.Describe_Lifted("2026-09-18 12:00 UTC"));
    }
}
