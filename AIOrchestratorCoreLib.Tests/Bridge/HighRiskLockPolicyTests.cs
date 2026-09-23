using AIOrchestratorCoreLib.Bridge.Decisions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// The three answers and the log line, as pure facts. The engine half — that they actually reach the
/// phone's terms, the default, the tap and the deadline sweep — is <c>HighRiskAndDeadlineProbeTests</c>'
/// (the <c>UnderClassic_</c> cases and every case that states the code on). Beside
/// <c>HighRiskClassifierTests</c> in <c>Tests/Bridge</c>, where every <c>Bridge.Decisions</c> test lives.
/// </summary>
public class HighRiskLockPolicyTests
{
    [Theory]
    [InlineData("push", false, true)]
    [InlineData(null, true, true)]
    [InlineData("push", true, true)]
    [InlineData(null, false, false)]
    public void Is_HighRisk_IsDeclaredOrDetected(string? matchedPattern, bool declared, bool expected)
    {
        Assert.Equal(expected, HighRiskLock_Policy.Is_HighRisk(matchedPattern, declared));
    }

    [Theory]
    [InlineData("push", false, true, true)]
    [InlineData(null, true, true, true)]
    [InlineData(null, false, true, false)]
    [InlineData("push", false, false, false)]
    [InlineData(null, true, false, false)]
    [InlineData(null, false, false, false)]
    public void Needs_Code_OnlyWhenHighRiskAndTheCodeIsOn(string? matchedPattern, bool declared, bool confirmationOn, bool expected)
    {
        Assert.Equal(expected, HighRiskLock_Policy.Needs_Code(matchedPattern, declared, confirmationOn));
    }

    /// <summary>
    /// RULING R21: A HIGH-RISK QUESTION NEVER TAKES A DEFAULT, and the setting is not a parameter here at
    /// all — so no value of it can bring a default back. The first cut of the switch let "merge and push"
    /// with <c>DEFAULT: 1</c> apply itself on timeout under classic.
    /// </summary>
    [Theory]
    [InlineData("push", false, 0, null)]
    [InlineData(null, true, 0, null)]
    [InlineData(null, false, 1, 1)]
    [InlineData(null, false, null, null)]
    public void Resolve_DefaultIndex_DropsTheDefaultOfEveryHighRiskQuestion(string? matchedPattern, bool declared, int? declaredDefault, int? expected)
    {
        Assert.Equal(expected, HighRiskLock_Policy.Resolve_DefaultIndex_OrNull(declaredDefault, matchedPattern, declared));
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

    /// <summary>Off, the line still names why it is high risk, says the code is off, and that no default is taken.</summary>
    [Fact]
    public void Describe_WithTheCodeOff_SaysOneTapDecides_AndNoDefault()
    {
        var line = HighRiskLock_Policy.Describe_OrNull("deploy", declaredHighRisk: false, confirmationOn: false)!;

        Assert.Contains("matched 'deploy'", line, StringComparison.Ordinal);
        Assert.Contains(HighRiskLock_Policy.CONFIRMATION_OFF_WORDS, line, StringComparison.Ordinal);
        Assert.Contains("no default", line, StringComparison.Ordinal);
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
