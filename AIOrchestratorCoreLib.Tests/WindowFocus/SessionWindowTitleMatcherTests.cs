using AIOrchestratorCoreLib.WindowFocus;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.WindowFocus;

/// <summary>
/// THE COLLISION THAT BROKE ORGANIZE (owner, 2026-08-20: it "doesn't really work").
///
/// Window titles were matched by plain substring, and `da-vinci-fintech-suite-1` is a substring of
/// `da-vinci-fintech-suite-10`. With ten orchestrations in one repo that is not a corner case, it is
/// the common one: Show focused the wrong terminal, Organize tiled the wrong window, and the
/// shutdown terminator — which closes windows by the same fragment — could close a session that was
/// still working.
/// </summary>
public class SessionWindowTitleMatcherTests
{
    [Fact]
    public void AnOrchestrationDoesNotMatchALongerIdThatStartsTheSameWay()
    {
        Assert.False(
            SessionWindowTitle_Matcher.Matches("SOLO · da-vinci-fintech-suite-10", "SOLO · da-vinci-fintech-suite-1"),
            "orchestration 1 matched orchestration 10's window — the exact collision this exists to stop");

        Assert.False(SessionWindowTitle_Matcher.Matches("SUP · crm-20", "SUP · crm-2"));
    }

    [Fact]
    public void AnExactTitleMatches()
    {
        Assert.True(SessionWindowTitle_Matcher.Matches("SOLO · da-vinci-fintech-suite-1", "SOLO · da-vinci-fintech-suite-1"));
    }

    /// <summary>
    /// A named orchestration carries its display name after another separator, and the fragment must
    /// still match — this is what most live windows look like.
    /// </summary>
    [Fact]
    public void ADisplayNameAfterTheIdStillMatches()
    {
        Assert.True(
            SessionWindowTitle_Matcher.Matches("SOLO · ai-orchestrator-8 · AI-Orch · Telegram UX", "SOLO · ai-orchestrator-8"),
            "a renamed topic's window stopped matching its own session");

        Assert.True(SessionWindowTitle_Matcher.Matches("SOLO · strategy-lab-4 · Capital injection bug", "SOLO · strategy-lab-4"));
    }

    /// <summary>Different roles in the same orchestration stay distinct.</summary>
    [Fact]
    public void RolesDoNotMatchEachOther()
    {
        Assert.False(SessionWindowTitle_Matcher.Matches("SUP · crm-2", "SOLO · crm-2"));
        Assert.False(SessionWindowTitle_Matcher.Matches("IMP-2 · crm-2", "IMP-1 · crm-2"));
    }

    /// <summary>An empty fragment matches nothing, rather than everything.</summary>
    [Fact]
    public void AnEmptyFragmentMatchesNothing()
    {
        Assert.False(SessionWindowTitle_Matcher.Matches("SOLO · crm-2", ""));
    }

    [Fact]
    public void AnUnrelatedWindowDoesNotMatch()
    {
        Assert.False(SessionWindowTitle_Matcher.Matches("Visual Studio Code", "SOLO · crm-2"));
    }
}
