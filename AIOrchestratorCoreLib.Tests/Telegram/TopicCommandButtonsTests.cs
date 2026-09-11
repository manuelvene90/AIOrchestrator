using System.Text;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// The owner wants the commands they reach for constantly to be permanent tappable furniture on
/// PULSE — an inline keyboard hanging off the status line.
///
/// TWO BARS SINCE 2026-09-09 (Brief C): an orchestration topic's bar (`TOPIC_BUTTONS`, about the ONE
/// endeavour it belongs to) and the General topic's bar (`GENERAL_BUTTONS`, cross-cutting across all
/// of them). Rendered twice each and parsed once through a single lexer is three places to drift, and
/// every drift is silent on the owner's side: a button that renders and does nothing, or a bar
/// offering a verb the lexer no longer knows. These tests pin both arrays to their commands, their
/// buttons and the one parser both bars share.
/// </summary>
public class TopicCommandButtonsTests
{
    /// <summary>Telegram's hard cap on callback_data — a payload over it is rejected at send time, on the phone.</summary>
    const int TELEGRAM_CALLBACK_DATA_BYTE_LIMIT = 64;

    /// <summary>A label wider than this stops being readable on a phone and starts wrapping.</summary>
    const int MAX_LABEL_LENGTH = 20;

    // ---------------------------------------------------------------------------------------
    // The orchestration topic's bar
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// REPLACES the pre-2026-09-09 array. /screen, /show, /pc and /test LEFT this bar on the owner's
    /// call: *"it is the six things they reach for from a phone while an endeavour runs, and looking
    /// at a Windows desktop is not one of them when they are not at it."* All four remain typed
    /// commands — see AnythingMalformed_IsNull below, which pins /show specifically as no longer a
    /// button — and the bar's new six are what is owed, what is happening, and what to do about it,
    /// two per row: pending, left, tail sup, limits, merge, close.
    /// </summary>
    [Fact]
    public void TheCommands_AreTheOnesTheOwnerAskedFor_InDisplayOrder()
    {
        Assert.Equal(new[] { "pending", "left", "tail sup", "limits", "merge", "close" }, TopicCommandButtons.Commands);
    }

    [Fact]
    public void TheInlineButtons_FollowTheSameOrderAsTheCommands()
    {
        var built = TopicCommandButtons.Build_ForTopic(4242L);

        Assert.Equal(TopicCommandButtons.Commands.Count, built.Count);
        Assert.Equal(
            TopicCommandButtons.Commands,
            built.Select(button => TopicCommandButtons.Parse_OrNull(button.Data)!.Value.Command).ToArray());
    }

    // ---------------------------------------------------------------------------------------
    // The General topic's bar — NEW, 2026-09-09
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// General has no session of its own, so its bar is the five cross-cutting questions the owner
    /// asks about the WHOLE machine rather than one endeavour: what happened everywhere, who wants
    /// me, how close to a limit, wake everything up, silence everything.
    /// </summary>
    [Fact]
    public void TheGeneralCommands_AreTheOnesTheOwnerAskedFor_InDisplayOrder()
    {
        Assert.Equal(new[] { "summary", "pending", "limits", "resume", "dnd_all" }, TopicCommandButtons.GeneralCommands);
    }

    [Fact]
    public void TheGeneralInlineButtons_FollowTheSameOrderAsTheGeneralCommands()
    {
        var built = TopicCommandButtons.Build_ForGeneral(0L);

        Assert.Equal(TopicCommandButtons.GeneralCommands.Count, built.Count);
        Assert.Equal(
            TopicCommandButtons.GeneralCommands,
            built.Select(button => TopicCommandButtons.Parse_OrNull(button.Data)!.Value.Command).ToArray());
    }

    /// <summary>
    /// A SEPARATE PROPERTY from <see cref="TopicCommandButtons.Commands"/> is load-bearing (its own
    /// doc comment says so, for EveryTopicButtonIsWiredTests's benefit) — this pins that the two lists
    /// genuinely do not share a command, so folding them together in a future edit would be a visible
    /// behaviour change here, not a silent one.
    /// </summary>
    [Fact]
    public void TheGeneralBarAndTheTopicBarOfferDifferentCommands_ApartFromTheSharedTwo()
    {
        // "pending" and "limits" are deliberately on BOTH bars — the owner asks each question either
        // about one endeavour or about all of them. Everything else is exclusive to its bar.
        var topicOnly = TopicCommandButtons.Commands.Except(TopicCommandButtons.GeneralCommands);
        var generalOnly = TopicCommandButtons.GeneralCommands.Except(TopicCommandButtons.Commands);

        Assert.Equal(new[] { "left", "tail sup", "merge", "close" }, topicOnly);
        Assert.Equal(new[] { "summary", "resume", "dnd_all" }, generalOnly);
    }

    // ---------------------------------------------------------------------------------------
    // Round trip
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The tap has to come back as the command it was drawn for, in the topic it was drawn in — a
    /// tap arrives with no text to infer either from, so if the payload does not carry them nothing
    /// downstream can reconstruct them.
    ///
    /// 0 is the General topic, which has no thread id: it must survive as 0, the convention the
    /// hold button and the receipt registry already share.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(4242L)]
    [InlineData(long.MaxValue)]
    public void EveryButton_RoundTripsWithItsTopic(long messageThreadId)
    {
        var built = TopicCommandButtons.Build_ForTopic(messageThreadId);

        for (var i = 0; i < built.Count; i++)
            Assert.Equal((TopicCommandButtons.Commands[i], messageThreadId), TopicCommandButtons.Parse_OrNull(built[i].Data));
    }

    /// <summary>The General bar's own round trip — untested before the bar existed.</summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(4242L)]
    public void EveryGeneralButton_RoundTripsWithItsTopic(long messageThreadId)
    {
        var built = TopicCommandButtons.Build_ForGeneral(messageThreadId);

        for (var i = 0; i < built.Count; i++)
            Assert.Equal((TopicCommandButtons.GeneralCommands[i], messageThreadId), TopicCommandButtons.Parse_OrNull(built[i].Data));
    }

    /// <summary>
    /// "tail sup" IS THE VERB, SPACE INCLUDED — the payload splits at the FIRST colon, so the target
    /// rides inside the verb rather than needing a third payload field. Asserted on its own, by name,
    /// because it is the one verb in either bar with a space in it and the round-trip theories above
    /// exercise it only incidentally.
    /// </summary>
    [Fact]
    public void TheTailSupVerbRoundTripsWithItsEmbeddedSpaceIntact()
    {
        var built = TopicCommandButtons.Build_ForTopic(4242L);
        var tailSup = built.Single(button => TopicCommandButtons.Parse_OrNull(button.Data)!.Value.Command == "tail sup");

        Assert.Equal("cmd:tail sup:4242", tailSup.Data);
        Assert.Equal(("tail sup", 4242L), TopicCommandButtons.Parse_OrNull(tailSup.Data));
    }

    /// <summary>
    /// A negative thread id cannot come from Telegram, whose ids are positive — but the type is
    /// long and this parser's null means "NOT OURS, fall through to the next handler". A payload
    /// that plainly is ours, rejected, falls through to handlers that will not recognise it either:
    /// the owner taps and nothing happens, silently. So the id is carried back verbatim and the
    /// component that holds the topic roster gets to say the topic does not exist, out loud.
    /// </summary>
    [Theory]
    [InlineData(-1L)]
    [InlineData(-4242L)]
    [InlineData(long.MinValue)]
    public void ANegativeTopic_IsCarriedBackVerbatimRatherThanSwallowed(long messageThreadId)
    {
        var built = TopicCommandButtons.Build_ForTopic(messageThreadId);

        Assert.Equal(("pending", messageThreadId), TopicCommandButtons.Parse_OrNull(built[0].Data));
    }

    // ---------------------------------------------------------------------------------------
    // Telegram's limits
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// 64 BYTES, not characters, and the rejection happens at send time on the phone where nothing
    /// here can see it. long.MaxValue and long.MinValue are the widest ids the type can hold.
    /// "tail sup" is the widest VERB either bar offers, so it is the one most likely to approach the
    /// limit as new commands are added.
    /// </summary>
    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    [InlineData(0L)]
    public void EveryTopicPayload_FitsTelegramsSixtyFourByteLimit(long messageThreadId)
    {
        foreach (var (data, _) in TopicCommandButtons.Build_ForTopic(messageThreadId))
            Assert.True(
                Encoding.UTF8.GetByteCount(data) <= TELEGRAM_CALLBACK_DATA_BYTE_LIMIT,
                $"callback data too long ({Encoding.UTF8.GetByteCount(data)} bytes): '{data}'");
    }

    /// <summary>The General bar's payloads are built the same way and need the same guarantee.</summary>
    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    [InlineData(0L)]
    public void EveryGeneralPayload_FitsTelegramsSixtyFourByteLimit(long messageThreadId)
    {
        foreach (var (data, _) in TopicCommandButtons.Build_ForGeneral(messageThreadId))
            Assert.True(
                Encoding.UTF8.GetByteCount(data) <= TELEGRAM_CALLBACK_DATA_BYTE_LIMIT,
                $"callback data too long ({Encoding.UTF8.GetByteCount(data)} bytes): '{data}'");
    }

    /// <summary>
    /// The label is read by a HUMAN on a phone: an emoji to find it with, then the slash command so
    /// the button's effect is unambiguous. A glyph alone leaves the owner guessing which of two
    /// similar pictures merges and which closes.
    /// </summary>
    [Fact]
    public void EveryTopicLabel_IsShortAndNamesItsCommand()
    {
        foreach (var (data, label) in TopicCommandButtons.Build_ForTopic(7L))
        {
            var command = TopicCommandButtons.Parse_OrNull(data)!.Value.Command;

            Assert.EndsWith("/" + command, label, StringComparison.Ordinal);
            Assert.True(label.Length < MAX_LABEL_LENGTH, $"label too long for a phone: '{label}'");

            // Emoji first, then the command — the eye scans the pictures, not the text.
            Assert.NotEqual('/', label[0]);
        }
    }

    /// <summary>Same rule, the General bar's labels — untested before the bar existed.</summary>
    [Fact]
    public void EveryGeneralLabel_IsShortAndNamesItsCommand()
    {
        foreach (var (data, label) in TopicCommandButtons.Build_ForGeneral(7L))
        {
            var command = TopicCommandButtons.Parse_OrNull(data)!.Value.Command;

            Assert.EndsWith("/" + command, label, StringComparison.Ordinal);
            Assert.True(label.Length < MAX_LABEL_LENGTH, $"label too long for a phone: '{label}'");
            Assert.NotEqual('/', label[0]);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Everything that is not ours
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// THE OTHER BUTTON FAMILIES ALREADY ON THIS BRIDGE. A tap this class cannot read must fall
    /// through to its own handler untouched — swallowing an "opt-{n}" as a malformed command would
    /// eat the owner's answer to a question, the one tap in this system that cannot be repeated.
    /// </summary>
    [Theory]
    [InlineData("hold:4242")]           // HoldButton_Data — pause delivery
    [InlineData("go:0")]                // HoldButton_Data — release
    [InlineData("close-yes-9f1c4a2b8d3e4f5a6b7c8d9e0f1a2b3c")]
    [InlineData("close-no-9f1c4a2b8d3e4f5a6b7c8d9e0f1a2b3c")]
    [InlineData("opt-0")]               // the single-use option registry
    [InlineData("opt-17")]
    public void TheOtherButtonFamilies_AreNotOurs(string callbackData)
    {
        Assert.Null(TopicCommandButtons.Parse_OrNull(callbackData));
    }

    /// <summary>
    /// And the other direction, because non-collision is a property of the PAIR: the hold parser
    /// must not claim our payloads either. Both prefixes have to stay unreadable to the other, or
    /// the parse ORDER in the tap handler silently decides what a payload means.
    /// </summary>
    [Fact]
    public void TheHoldParser_DoesNotClaimOurPayloads()
    {
        foreach (var (data, _) in TopicCommandButtons.Build_ForTopic(4242L))
            Assert.Null(HoldButton_Data.Parse_OrNull(data));
    }

    /// <summary>
    /// Malformed input returns null; it never throws. This runs on a wire payload, and an exception
    /// on the tap path takes down the handling of every OTHER tap in the same batch.
    ///
    /// "pending" replaces "show" as the shape-test placeholder verb (no topic, empty topic,
    /// non-numeric, stray whitespace) since "show" is no longer a command either bar offers — using a
    /// live verb keeps these cases testing SHAPE alone, not shape-plus-unknown-verb at once.
    /// "cmd:show:5" is kept as its OWN case, deliberately: /show LEFT the topic bar on 2026-09-09 and
    /// remains only a typed command, so a well-formed payload for it must now be refused exactly like
    /// "cmd:pause:5" always was.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("cmd:")]                    // no verb, no topic
    [InlineData("cmd:pending")]             // a verb with no topic
    [InlineData("cmd:pending:")]            // a verb with an empty topic
    [InlineData("cmd:pending:notanumber")]
    [InlineData("cmd:pending: 5")]          // whitespace is not something Build_ForTopic ever wrote
    [InlineData("cmd::5")]                  // a topic with no verb
    [InlineData("cmd:pause:5")]             // a verb this class has never offered
    [InlineData("cmd:show:5")]              // /show LEFT the bar 2026-09-09 — typed command only now
    [InlineData("cmd:PENDING:5")]           // ordinal, case-sensitive
    [InlineData("CMD:pending:5")]
    [InlineData("cmd")]
    [InlineData("pending:5")]               // the prefix is what makes it ours
    public void AnythingMalformed_IsNull(string? callbackData)
    {
        Assert.Null(TopicCommandButtons.Parse_OrNull(callbackData));
    }
}
