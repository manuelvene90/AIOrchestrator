using System.Text;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// The owner wants the commands they reach for constantly to be permanent tappable furniture in
/// every topic — an inline keyboard on the status line AND a bar above the input box.
///
/// The same commands rendered twice and parsed once is three places to drift, and every drift
/// is silent on the owner's side: a button that renders and does nothing, or a bar offering a verb
/// the lexer no longer knows. These tests pin the three to one array.
/// </summary>
public class TopicCommandButtonsTests
{
    /// <summary>Telegram's hard cap on callback_data — a payload over it is rejected at send time, on the phone.</summary>
    const int TELEGRAM_CALLBACK_DATA_BYTE_LIMIT = 64;

    /// <summary>A label wider than this stops being readable on a phone and starts wrapping.</summary>
    const int MAX_LABEL_LENGTH = 20;

    /// <summary>What the reply keyboard chunks by - mirrors TopicCommandButtons' own column count.</summary>
    const int REPLY_KEYBOARD_COLUMNS = 2;

    /// <summary>
    /// /pc and /close REPLACED /refresh on the owner's call, 2026-09-07: *"Let's remove the /refresh
    /// button from the pulse message, an place the /close and /pc command as buttons instead."*
    /// /refresh remains a typed command and keeps its entry in Telegram's "/" menu; only the
    /// standing button went.
    /// </summary>
    [Fact]
    public void TheCommands_AreTheOnesTheOwnerAskedFor_InDisplayOrder()
    {
        Assert.Equal(new[] { "screen", "show", "merge", "test", "pc", "close", "pause", "progress" }, TopicCommandButtons.Commands);
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

        Assert.Equal(("screen", messageThreadId), TopicCommandButtons.Parse_OrNull(built[0].Data));
    }

    // ---------------------------------------------------------------------------------------
    // Telegram's limits
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// 64 BYTES, not characters, and the rejection happens at send time on the phone where nothing
    /// here can see it. long.MaxValue and long.MinValue are the widest ids the type can hold.
    /// </summary>
    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    [InlineData(0L)]
    public void EveryPayload_FitsTelegramsSixtyFourByteLimit(long messageThreadId)
    {
        foreach (var (data, _) in TopicCommandButtons.Build_ForTopic(messageThreadId))
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
    public void EveryLabel_IsShortAndNamesItsCommand()
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

    // ---------------------------------------------------------------------------------------
    // The reply keyboard
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Telegram sends a reply button's TEXT verbatim as an ordinary message, so these strings are
    /// not labels — they are the messages the app's command lexer will have to recognise. An emoji
    /// or a stray space here arrives as part of the message and the command is not recognised.
    /// </summary>
    [Fact]
    public void TheReplyKeyboard_IsExactlyTheSlashCommands_TwoPerRow()
    {
        var rows = TopicCommandButtons.Build_ReplyKeyboardRows();

        Assert.Equal(4, rows.Count);
        Assert.Equal(new[] { "/screen", "/show" }, rows[0]);
        Assert.Equal(new[] { "/merge", "/test" }, rows[1]);
        Assert.Equal(new[] { "/pc", "/close" }, rows[2]);
        Assert.Equal(new[] { "/pause", "/progress" }, rows[3]);
    }


    /// <summary>
    /// THE ROW THE OWNER ASKED FOR, 2026-09-09: *"it should also become a new button under the pulse
    /// button, and since we need two buttons per row, let's also add another command that you think
    /// is useful."* /pause alone would have left a half-empty row, so the count staying EVEN is the
    /// requirement, not decoration — and it is stated here rather than inferred from the row shapes,
    /// which is the mistake this file already made once with an odd command count.
    /// </summary>
    [Fact]
    public void TheCommandCount_StaysEven_SoNoRowIsLeftHalfEmpty()
    {
        Assert.Equal(0, TopicCommandButtons.Commands.Count % REPLY_KEYBOARD_COLUMNS);
    }

    /// <summary>
    /// /pause and /progress share the last row deliberately: the one that changes state is paired
    /// with a READ-ONLY one, rather than putting a second consequential button beside /close.
    /// </summary>
    [Fact]
    public void PauseAndProgress_AreTheLastRow_AndPauseComesFirst()
    {
        var commands = TopicCommandButtons.Commands;

        Assert.Equal("pause", commands[commands.Count - 2]);
        Assert.Equal("progress", commands[commands.Count - 1]);
    }

    /// <summary>
    /// NO ROW IS EVER PADDED: a blank button would be a tap target that does nothing. This used to
    /// be read off the data - the old fifth command left a one-button last row, and the assertion
    /// on that row carried the property by accident. Six commands divide evenly, so the row shapes
    /// no longer show it. Stating it as an invariant is what keeps it covered instead of letting it
    /// lapse the moment the count stopped being odd.
    /// </summary>
    [Fact]
    public void TheReplyKeyboard_NeverPadsARow()
    {
        var rows = TopicCommandButtons.Build_ReplyKeyboardRows();

        Assert.All(rows, row => Assert.InRange(row.Count, 1, REPLY_KEYBOARD_COLUMNS));
        Assert.Equal(TopicCommandButtons.Commands.Count, rows.Sum(row => row.Count));
    }

    /// <summary>
    /// Nothing decorative: whatever is in the string lands in the chat as the owner's own message.
    /// </summary>
    [Fact]
    public void NoReplyKeyboardButton_CarriesDecoration()
    {
        foreach (var text in TopicCommandButtons.Build_ReplyKeyboardRows().SelectMany(row => row))
        {
            Assert.StartsWith("/", text, StringComparison.Ordinal);
            Assert.Equal(text.Trim(), text);
            Assert.All(text, character => Assert.True(character < 128, $"non-ASCII in a reply button: '{text}'"));
        }
    }

    /// <summary>
    /// The two renderings are the whole reason this class exists: they must offer the SAME four
    /// commands, in the same order. A fifth command added to one and not the other is the drift the
    /// single source of truth is here to make impossible.
    /// </summary>
    [Fact]
    public void TheTwoRenderings_OfferTheSameCommandsInTheSameOrder()
    {
        var inline = TopicCommandButtons.Build_ForTopic(4242L)
            .Select(button => TopicCommandButtons.Parse_OrNull(button.Data)!.Value.Command);

        var reply = TopicCommandButtons.Build_ReplyKeyboardRows()
            .SelectMany(row => row)
            .Select(text => text[1..]);

        Assert.Equal(inline, reply);
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
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("cmd:")]                    // no verb, no topic
    [InlineData("cmd:show")]                // a verb with no topic
    [InlineData("cmd:show:")]               // a verb with an empty topic
    [InlineData("cmd:show:notanumber")]
    [InlineData("cmd:show: 5")]             // whitespace is not something Build_ForTopic ever wrote
    [InlineData("cmd::5")]                  // a topic with no verb
    // "pause" USED TO BE THE EXAMPLE HERE, and it stopped being one the day the owner asked for
    // the button (2026-09-09) — the case then asserted that a real command was unparseable, and
    // it failed loudly, which is what a case naming its intent is for. Any stand-in must be a
    // verb nobody would plausibly add.
    [InlineData("cmd:defenestrate:5")]      // a verb this class does not offer
    [InlineData("cmd:SHOW:5")]              // ordinal, case-sensitive
    [InlineData("CMD:show:5")]
    [InlineData("cmd")]
    [InlineData("show:5")]                  // the prefix is what makes it ours
    public void AnythingMalformed_IsNull(string? callbackData)
    {
        Assert.Null(TopicCommandButtons.Parse_OrNull(callbackData));
    }
}
