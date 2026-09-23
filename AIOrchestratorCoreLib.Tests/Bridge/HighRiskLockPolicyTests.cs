using AIOrchestratorCoreLib.Bridge.Decisions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// The lock and its log line, as pure facts. The engine half — that the lock actually reaches the
/// phone's terms, the default and the tap — is <c>HighRiskAndDeadlineProbeTests</c>' (the two
/// <c>UnderClassic_</c> cases and every case that states the code on). Beside
/// <c>HighRiskClassifierTests</c> in <c>Tests/Bridge</c>, where every <c>Bridge.Decisions</c> test lives.
/// </summary>
public class HighRiskLockPolicyTests
{
    [Theory]
    [InlineData("push", false, true, true)]
    [InlineData(null, true, true, true)]
    [InlineData("push", true, true, true)]
    [InlineData(null, false, true, false)]
    [InlineData("push", false, false, false)]
    [InlineData(null, true, false, false)]
    [InlineData(null, false, false, false)]
    public void Is_Locked_OnlyWhenHighRiskAndTheCodeIsOn(string? matchedPattern, bool declared, bool confirmationOn, bool expected)
    {
        Assert.Equal(expected, HighRiskLock_Policy.Is_Locked(matchedPattern, declared, confirmationOn));
    }

    /// <summary>The two lines the engine wrote before the switch existed, unchanged — a log reader greps them.</summary>
    [Fact]
    public void Describe_WithTheCodeOn_KeepsTheExistingWording()
    {
        Assert.Equal(
            "Question classified HIGH RISK (matched 'push') — a tap will require the read-back code",
            HighRiskLock_Policy.Describe_OrNull("push", declaredHighRisk: false, confirmationOn: true));

        Assert.Equal(
            "Question classified HIGH RISK (declared by the asker, no pattern matched) — a tap will require the read-back code",
            HighRiskLock_Policy.Describe_OrNull(null, declaredHighRisk: true, confirmationOn: true));
    }

    /// <summary>Off, the line still names what would have locked, and says the code is off.</summary>
    [Fact]
    public void Describe_WithTheCodeOff_NamesWhatWouldHaveLocked()
    {
        var line = HighRiskLock_Policy.Describe_OrNull("deploy", declaredHighRisk: false, confirmationOn: false)!;

        Assert.Contains("matched 'deploy'", line, StringComparison.Ordinal);
        Assert.Contains(HighRiskLock_Policy.CONFIRMATION_OFF_WORDS, line, StringComparison.Ordinal);
        Assert.DoesNotContain("will require", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Describe_AnOrdinaryQuestion_HasNoLine(bool confirmationOn)
    {
        Assert.Null(HighRiskLock_Policy.Describe_OrNull(null, declaredHighRisk: false, confirmationOn));
    }
}
