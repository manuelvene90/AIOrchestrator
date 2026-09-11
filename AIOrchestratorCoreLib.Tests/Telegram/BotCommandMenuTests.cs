using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// THE MENU IS USER INTERFACE, SO IT IS ASSERTED — brief F2.
///
/// <para>
/// <c>setMyCommands</c> preserves order, so the list IS the thing the owner scrolls on their phone.
/// Inline in a 13 000-line engine method nothing could reach it; the reorder is the whole change,
/// and a reorder with no test is a change that reverts itself the next time someone appends.
/// </para>
/// </summary>
public class BotCommandMenuTests
{
    /// <summary>
    /// The owner's own order (2026-09-09), by frequency of use rather than by theme. Written out
    /// in full rather than derived: a test that recomputed the order from the same source would
    /// assert nothing at all.
    /// </summary>
    static readonly string[] EXPECTED_ORDER =
    [
        "pending", "left", "progress", "tail", "limits", "cost", "merge", "close", "dnd", "mute",
        "summary", "resume", "status", "tasks", "tokens", "context", "diff", "imp", "log", "test",
        "done", "refresh", "switch", "clear",
        "pc", "dnd_all", "mute_all", "screens", "screen", "show", "organize", "organize_mains",
    ];

    [Fact]
    public void TheMenu_IsInTheOwnersUseOrder()
    {
        Assert.Equal(EXPECTED_ORDER, BotCommandMenu.ALL.Select(entry => entry.Command));
    }

    /// <summary>
    /// NOTHING WAS DROPPED IN THE REORDER, which is the one thing a reshuffle can silently do. The
    /// count is stated as a number as well as a set, because "same set" would still pass if a
    /// duplicate replaced a missing one.
    /// </summary>
    [Fact]
    public void EveryCommand_SurvivedTheReorder_AndNoneTwice()
    {
        Assert.Equal(32, BotCommandMenu.ALL.Count);
        Assert.Equal(32, BotCommandMenu.ALL.Select(entry => entry.Command).Distinct().Count());
    }

    /// <summary>
    /// EVERY HOST-GATED COMMAND IS AT THE VERY BOTTOM, contiguously, with nothing portable after
    /// it — on the VPS these four answer "not available on this host yet", so none of them may sit
    /// among the commands the owner actually uses.
    ///
    /// <para>
    /// The boundary is DERIVED, not typed: an earlier version asserted <c>index >= 24</c>, which
    /// would have failed the day any unrelated command left the daily block, for a reason that has
    /// nothing to do with what this case is about.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryHostGatedCommand_SitsInTheTail_WithNothingPortableBelowIt()
    {
        var commands = BotCommandMenu.ALL.Select(entry => entry.Command).ToList();
        var firstGated = commands.FindIndex(BotCommandMenu.HOST_GATED.Contains);
        var lastPortable = commands.FindLastIndex(command => !BotCommandMenu.HOST_GATED.Contains(command));

        Assert.True(firstGated > lastPortable,
            $"a host-gated command sits above a portable one: {string.Join(", ", commands)}");

        Assert.Equal(BotCommandMenu.HOST_GATED.Count, commands.Count - firstGated);
    }

    /// <summary>
    /// The list names commands that exist, and ONLY the ones the engine really refuses. It is a
    /// second reading of <c>Refuse_IfNoWindowing_Async</c>'s call sites (the engine is
    /// <c>internal sealed</c>, so it cannot be asked); its first version wrongly swept in
    /// <c>/pc</c> and <c>/screens</c>, which are ordinary toggles that work on any host.
    /// </summary>
    [Fact]
    public void TheHostGatedList_NamesRealCommands_AndNotTheTogglesBesideThem()
    {
        foreach (var command in BotCommandMenu.HOST_GATED)
            Assert.Contains(command, BotCommandMenu.ALL.Select(entry => entry.Command));

        Assert.DoesNotContain("pc", BotCommandMenu.HOST_GATED);
        Assert.DoesNotContain("screens", BotCommandMenu.HOST_GATED);
    }

    /// <summary>
    /// Telegram refuses a command that is not 1-32 characters of lowercase letters, digits and
    /// underscores, and it refuses a description outside 3-256 characters — for the WHOLE call, so
    /// one bad entry costs the entire menu, in every scope, silently (the caller logs a warning
    /// nobody reads).
    /// </summary>
    [Fact]
    public void EveryEntry_IsOneTelegramWillAccept()
    {
        foreach (var (command, description) in BotCommandMenu.ALL)
        {
            Assert.InRange(command.Length, 1, 32);
            Assert.True(command.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'), $"'{command}' is not a legal command name");
            Assert.InRange(description.Length, 3, 256);
        }
    }

    /// <summary>
    /// APP-WRITTEN STRINGS ARE ENGLISH (owner, 2026-09-09). Checked as the absence of the accented
    /// letters Italian would bring, which is a cheap proxy that would have caught the one way this
    /// actually regresses: someone answering the owner in their language and editing the menu to
    /// match. Emoji and en dashes are deliberately allowed — several descriptions use them.
    /// </summary>
    [Fact]
    public void TheDescriptions_AreEnglish()
    {
        foreach (var (command, description) in BotCommandMenu.ALL)
        {
            Assert.DoesNotContain(description, c => "àèéìòùÀÈÉÌÒÙ".Contains(c));
            Assert.False(string.IsNullOrWhiteSpace(description), $"'{command}' has no description");
        }
    }
}
