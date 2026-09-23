using AIOrchestratorCoreLib.Bridge.CommandBars;
using AIOrchestratorCoreLib.Configuration.PulseSettings;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.CommandBars;

/// <summary>
/// The engine's half of the bars, asked directly (plan 03 Task 5). The engine tests in
/// ConfigurableCommandButtonsTests prove the values reach the phone; these pin what that component
/// owns and an engine test would need many ticks to see: the rows of two, and a refusal said ONCE per
/// distinct line however many times a bar is rebuilt — every status tick rebuilds every bar.
/// </summary>
public class CommandBarsTests
{
    const long TOPIC_ID = 4242;

    readonly RecordingLog_Fake _log = new();
    readonly ICommandBars _bars;

    public CommandBarsTests()
    {
        _bars = CommandBars_Factory.Create(_log);
    }

    [Fact]
    public void Build_TopicRows_ChunksTwoPerRow_WithTheToggleLast()
    {
        var rows = _bars.Build_TopicRows(Pulse(["pending", "left", "limits"], holdToggle: true), ReceiptStyles.Ticks, TOPIC_ID, isHolding: false, heldCount: 0);

        Assert.Equal(
            [["cmd:pending:4242", "cmd:left:4242"], ["cmd:limits:4242", HoldButton_Data.Build(HoldButtonActions.Hold, TOPIC_ID)]],
            rows.Select(row => row.Select(button => button.Data).ToArray()).ToArray());
    }

    /// <summary>D4: an empty <c>general.buttons</c> is no rows, which the client sends as no <c>reply_markup</c>.</summary>
    [Fact]
    public void Build_GeneralRows_OfAnEmptyList_IsNoRows()
    {
        Assert.Empty(_bars.Build_GeneralRows(Pulse([], holdToggle: true, generalButtons: [])));
    }

    [Fact]
    public void Build_GeneralRows_UsesThreadZero_AndRowsOfTwo()
    {
        var rows = _bars.Build_GeneralRows(Pulse([], holdToggle: true, generalButtons: ["summary", "pending", "limits"]));

        Assert.Equal(
            [["cmd:summary:0", "cmd:pending:0"], ["cmd:limits:0"]],
            rows.Select(row => row.Select(button => button.Data).ToArray()).ToArray());
    }

    /// <summary>
    /// ONCE PER DISTINCT LINE: the same refusal across many rebuilds is one line; an EDIT that names a
    /// different verb is a new line and is said; going back to a list already said stays quiet.
    /// </summary>
    [Fact]
    public void ARefusedVerb_IsSaidOnce_AcrossRebuilds_AndANewRefusalIsSaidWhenItAppears()
    {
        for (var tick = 0; tick < 5; tick++)
            _bars.Build_TopicRows(Pulse(["pending", "tasks"], holdToggle: true), ReceiptStyles.Ticks, TOPIC_ID, isHolding: false, heldCount: 0);

        Assert.Equal(1, Count_Warnings("pulse.buttons names 'tasks'"));

        _bars.Build_TopicRows(Pulse(["pending", "mute_all"], holdToggle: true), ReceiptStyles.Ticks, TOPIC_ID, isHolding: false, heldCount: 0);
        _bars.Build_TopicRows(Pulse(["pending", "tasks"], holdToggle: true), ReceiptStyles.Ticks, TOPIC_ID, isHolding: false, heldCount: 0);

        Assert.Equal(1, Count_Warnings("pulse.buttons names 'mute_all'"));
        Assert.Equal(1, Count_Warnings("pulse.buttons names 'tasks'"));
        Assert.Equal(2, Count_Warnings("no tap can run"));
    }

    /// <summary>
    /// D10's fallback is said ONCE, whichever of the two homes asks first and however often both ask —
    /// the bar every tick, the receipt on every owner message. It lands in the MACHINE log (the empty
    /// scope), because both keys are machine settings and no one orchestration owns the line.
    /// </summary>
    [Fact]
    public void TheD10Fallback_IsSaidOnce_WhicheverHomeAsks_AndPutsTheToggleOnTheBar()
    {
        var pulse = Pulse(TopicCommandButtons.Commands, holdToggle: false);

        for (var tick = 0; tick < 3; tick++)
        {
            Assert.True(_bars.Is_HoldToggleOnTheBar(pulse, ReceiptStyles.Reactions));

            var bar = _bars.Build_TopicRows(pulse, ReceiptStyles.Reactions, TOPIC_ID, isHolding: false, heldCount: 0);

            Assert.Equal(HoldButton_Data.Build(HoldButtonActions.Hold, TOPIC_ID), bar[^1][^1].Data);
        }

        Assert.Equal(1, Count_Warnings(HoldTogglePlacement_Resolver.FALLBACK_WARNING));
        Assert.Contains($"WARN  [] {HoldTogglePlacement_Resolver.FALLBACK_WARNING}", _log.Dump(), StringComparison.Ordinal);
        Assert.Contains("pulse.holdToggle", HoldTogglePlacement_Resolver.FALLBACK_WARNING, StringComparison.Ordinal);
        Assert.Contains("phone.receipts", HoldTogglePlacement_Resolver.FALLBACK_WARNING, StringComparison.Ordinal);
    }

    /// <summary>Both presets' own pairs are coherent, and a coherent pair has nothing to say.</summary>
    [Theory]
    [InlineData(false, ReceiptStyles.Ticks, false)]
    [InlineData(true, ReceiptStyles.Reactions, true)]
    [InlineData(true, ReceiptStyles.Ticks, true)]
    public void ACoherentPair_IsHonoured_AndSaysNothing(bool holdToggle, ReceiptStyles receipts, bool expectedOnTheBar)
    {
        Assert.Equal(expectedOnTheBar, _bars.Is_HoldToggleOnTheBar(Pulse(TopicCommandButtons.Commands, holdToggle), receipts));
        Assert.Equal(string.Empty, _log.Dump());
    }

    static IPulseSettings Pulse(IReadOnlyList<string> buttons, bool holdToggle, IReadOnlyList<string>? generalButtons = null)
    {
        return PulseSettings_Factory.Create(
            fields: [],
            stepMinutes: 5,
            buttons: buttons,
            generalButtons: generalButtons ?? TopicCommandButtons.GeneralCommands,
            holdToggle: holdToggle);
    }

    int Count_Warnings(string fragment)
    {
        return _log.Dump()
            .Split(Environment.NewLine)
            .Count(line => line.StartsWith("WARN", StringComparison.Ordinal) && line.Contains(fragment, StringComparison.Ordinal));
    }
}
