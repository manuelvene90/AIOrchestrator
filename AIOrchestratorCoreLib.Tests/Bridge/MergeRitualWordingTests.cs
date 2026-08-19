using AIOrchestratorCoreLib.Bridge;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// /merge HANDS THE SESSION A CONTRACT, and the order inside it is the safety.
///
/// The owner asks for this operation "very often" (2026-08-19) and had been spelling it out each
/// time — which is how a step gets dropped, because the wording varied and so did what happened.
///
/// They chose the session over the app once the trade was named: the app has no build step and no
/// test runner, so an app-side merge is this ritual minus the one check that catches a merge which
/// is green on both sides and red once combined.
/// </summary>
public class MergeRitualWordingTests
{
    [Fact]
    public void TheSuiteRunsOnTheMergedTree_BeforeAnyPush()
    {
        var text = MergeRitual_Wording.Build();

        var suite = text.IndexOf("ON THE MERGED TREE", StringComparison.Ordinal);
        var push = text.IndexOf("Push only once it is green", StringComparison.Ordinal);

        Assert.True(suite >= 0, "the ritual never tells the session to test the merged tree — the reason it is the session doing this at all");
        Assert.True(push >= 0, "the ritual never says when to push");

        Assert.True(suite < push, "the push is described before the suite: a session reading in order would push an untested merge");
    }

    /// <summary>
    /// NOTHING IS DELETED BEFORE IT IS ON THE REMOTE, and nothing is deleted with `-D`. A ritual that
    /// tidied first would turn a failed merge into lost work.
    /// </summary>
    [Fact]
    public void CleanupComesAfterThePush_AndRefusesTheForcefulDelete()
    {
        var text = MergeRitual_Wording.Build();

        var push = text.IndexOf("Push only once it is green", StringComparison.Ordinal);
        var clean = text.IndexOf("Then clean", StringComparison.Ordinal);

        Assert.True(push < clean, "cleanup is described before the push — a failed merge would take the branches with it");

        Assert.Contains("`git branch -d`", text);
        Assert.Contains("never", text);
        Assert.Contains("-D", text);
    }

    /// <summary>
    /// A red suite STOPS the ritual. Without this the session would read "push" as the next step and
    /// a broken master is the one outcome this whole command must not produce.
    /// </summary>
    [Fact]
    public void ARedSuiteStopsEverything()
    {
        var text = MergeRitual_Wording.Build();

        Assert.Contains("STOP", text);
        Assert.Contains("Do not push", text);
    }

    /// <summary>
    /// The honesty clause, and it is not decoration: tonight's own cleanup had NO remote branches to
    /// delete, and "remote branches cleaned" would have been a true-sounding empty claim the owner
    /// could not check from a phone.
    /// </summary>
    [Fact]
    public void ItDemandsTheReportSayWhatWasActuallyTrue()
    {
        Assert.Contains("SAY WHAT WAS TRUE, NOT WHAT SOUNDS COMPLETE", MergeRitual_Wording.Build());
    }
}
