using AIOrchestratorCoreLib.Bridge.PeriodicStatus;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.PeriodicStatus;

/// <summary>
/// MASTER'S TEXT, RE-PORTED — the shape <c>Build_PeriodicStatusText</c> had in <c>14c1cdb^</c> before the
/// fork deleted it: the bare STATUS header, the /status roster, and the ledger's current task. The
/// roster below is written in the wording the engine's roster builder produces (counts line with the
/// deltas since the last status, supervisor row, one row per member), so the expected string is also
/// the sample of what reaches the phone under classic.
/// </summary>
public class PeriodicStatusBuilderTests
{
    const string ROSTER =
        "AI Orchestrator: 17(+1)/30 done (56% +3%) · 1 running\n" +
        "- supervisor: idle — waiting · ctx 41%\n" +
        "- imp-1: working now · ctx 85% · last wrote 3 min ago\n" +
        "- rev-1: closed";

    /// <summary>
    /// The task goes through the card summariser master used, which drops the ledger's own "3."
    /// numbering and caps it at <c>CARD_TASK_WORDS</c> — the same words the app's cards show.
    /// </summary>
    [Fact]
    public void Build_WithATaskInProgress_EndsWithTheNowLine()
    {
        Assert.Equal(
            "STATUS\n" + ROSTER + "\n- now: wire the UI",
            PeriodicStatus_Builder.Build(ROSTER, "3. wire the UI"));
    }

    /// <summary>No <c>[&gt;]</c> line: no "now:" line — never "now: (none)".</summary>
    [Fact]
    public void Build_WithNothingInProgress_IsTheHeaderAndTheRoster()
    {
        Assert.Equal("STATUS\n" + ROSTER, PeriodicStatus_Builder.Build(ROSTER, currentTaskText: null));
    }

    /// <summary>
    /// THE HEADER IS THE CHANNEL SUBJECT'S WORD, from the one constant — the mirror recognises a status
    /// entry by that prefix and renders its body without the ⚙ App label, so a second spelling here
    /// would put the label back on every status the day someone edits one of them.
    /// </summary>
    [Fact]
    public void Header_IsTheMirrorsStatusSubject()
    {
        Assert.Equal(AIOrchestratorCoreLib.Mirroring.MirrorText_Formatter.STATUS_SUBJECT_PREFIX, PeriodicStatus_Builder.HEADER);
    }
}
