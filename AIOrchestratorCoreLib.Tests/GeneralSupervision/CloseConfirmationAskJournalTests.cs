using AIOrchestratorCoreLib.GeneralSupervision.ParkedCloseRequest;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.GeneralSupervision;

/// <summary>
/// THE JOURNAL HAS TO SAY WHICH PROMPT THIS IS.
///
/// <para>
/// On the VPS on 2026-09-06 the line was identical for a first ask and for the fresh prompt the sweep
/// posts after a restart has killed the previous keyboard. Reading the journal afterwards there was
/// no way to tell them apart, so the tap that closed <c>sandbox-1</c> could not be attributed to
/// either keyboard — and the VPS report's claim that the PRE-restart keyboard still worked rested
/// entirely on that ambiguity (the code says it cannot: the registry it is matched against starts
/// empty).
/// </para>
/// <para>
/// AGE ALONE WOULD PRODUCE A FALSE SENTENCE, which is why the three states below take two more facts.
/// A request parked while Telegram was unreachable, or before its orchestration had a topic, waits for
/// hours and is then asked for the FIRST time; "asked again" would be a lie about exactly the case a
/// reader is most likely to be investigating.
/// </para>
/// </summary>
public class CloseConfirmationAskJournalTests
{
    static readonly DateTime PREVIOUS_PROMPT = new(2026, 9, 6, 22, 8, 0, DateTimeKind.Utc);

    static IParkedCloseRequest Build_Request()
    {
        return ParkedCloseRequest_Factory.Create_ForOrchestration(
            orchId: "sandbox-1",
            requester: "supervisor of sandbox-1",
            reason: "the work is done",
            parkedFilePath: "/sup/.requests/awaiting-owner/close-1.json");
    }

    /// <summary>
    /// The common case, and the only one that may read as a plain statement: nothing was asked before,
    /// and the age is REPORTED rather than interpreted — so an old request asked for the first time
    /// (Telegram was down, the topic did not exist yet) reads honestly instead of being called a re-ask.
    /// </summary>
    [Fact]
    public void AFirstAsk_SaysSo_AndNamesTheAge_WithoutClaimingAnEarlierPrompt()
    {
        var line = CloseConfirmationPrompt_Builder.Describe_AskForTheJournal(
            Build_Request(), TimeSpan.FromSeconds(2), promptFromABygoneProcessUtc: null, alreadyAskedInThisRun: false);

        Assert.Contains("first prompt for this request", line);
        Assert.DoesNotContain("AGAIN", line);
        Assert.Contains("sandbox-1", line);
        Assert.Contains("supervisor of sandbox-1", line);
    }

    /// <summary>
    /// The line the VPS could not write. It names the previous prompt's own time, so a journal reader
    /// can line it up against the kill and see that the keyboard from before it is dead.
    /// </summary>
    [Fact]
    public void AReAskAfterARestart_SaysAGAIN_AndDatesTheDeadKeyboard()
    {
        var line = CloseConfirmationPrompt_Builder.Describe_AskForTheJournal(
            Build_Request(), TimeSpan.FromHours(3), PREVIOUS_PROMPT, alreadyAskedInThisRun: false);

        Assert.Contains("AGAIN", line);
        Assert.Contains("2026-09-06 22:08", line);
        Assert.Contains("buttons are dead", line);
        Assert.Contains("3 h", line);
    }

    /// <summary>
    /// The third state, and the reason "AGAIN" alone is not enough: a prompt dropped by THIS host —
    /// a cleared topic takes its message with it — is also a re-ask, but blaming a restart for it
    /// would put a wrong cause in the journal that reads exactly like the right one.
    /// </summary>
    [Fact]
    public void AReAskAfterThisHostDroppedItsOwnPrompt_SaysAGAIN_ButBlamesNoRestart()
    {
        var line = CloseConfirmationPrompt_Builder.Describe_AskForTheJournal(
            Build_Request(), TimeSpan.FromMinutes(40), promptFromABygoneProcessUtc: null, alreadyAskedInThisRun: true);

        Assert.Contains("AGAIN", line);
        Assert.Contains("this host's earlier prompt was dropped", line);
        Assert.DoesNotContain("when this host last stopped", line);
    }

    /// <summary>
    /// A restart takes precedence over the in-run flag when both are true. The restart is the older
    /// and more surprising fact, and it is the one that explains a keyboard the owner can still see.
    /// </summary>
    [Fact]
    public void WhenBothAreTrue_TheRestartIsTheOneReported()
    {
        var line = CloseConfirmationPrompt_Builder.Describe_AskForTheJournal(
            Build_Request(), TimeSpan.FromHours(1), PREVIOUS_PROMPT, alreadyAskedInThisRun: true);

        Assert.Contains("when this host last stopped", line);
    }
}
