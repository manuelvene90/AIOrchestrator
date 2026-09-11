using System.Globalization;
using AIOrchestratorCoreLib.Bridge.Decisions;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// The owner, 2026-08-24: *"buttons don't wrap, so when a session asks me a question I often can't
/// read all the button text."* Telegram cuts an inline button label instead of wrapping it, so two
/// carefully written options can arrive as two buttons reading the same first 30 characters — and
/// the owner has to guess which one they are tapping.
///
/// These tests pin both halves of the answer: the long case moves the full texts into the message
/// body as a numbered list and leaves numbers on the buttons, and the SHORT case changes absolutely
/// nothing, because today's behaviour is right whenever the text already fits.
/// </summary>
public class OptionButtonsLayoutTests
{
    /// <summary>Exactly at the limit — the last input that must still be left alone.</summary>
    const string TWENTY_EIGHT_CHARACTERS = "abcdefghijklmnopqrstuvwxyz12";

    /// <summary>One character over — the first input that must trigger the numbered list.</summary>
    const string TWENTY_NINE_CHARACTERS = "abcdefghijklmnopqrstuvwxyz123";

    const string A_LONG_OPTION = "Rebase the branch onto master and re-run the whole suite before merging";

    const string ANOTHER_LONG_OPTION = "Merge it as it stands and open a follow-up for the flaky download test";

    // ─────────────────────────── the no-change path ───────────────────────────

    /// <summary>
    /// THE PATH THAT MUST NOT MOVE. Three short answers need no list and no numbering: the labels
    /// come back exactly as the agent wrote them and the message body gains nothing. A refactor that
    /// makes numbering unconditional would fail here, which is the point of the test.
    /// </summary>
    [Fact]
    public void ShortOptions_KeepTheirOwnTextAndAddNothingToTheMessage()
    {
        string[] options = ["Yes", "No", "Ask me later"];

        var (buttonLabels, optionListText) = OptionButtons_Layout.Build(options);

        Assert.Equal(options, buttonLabels);
        Assert.Null(optionListText);
    }

    /// <summary>An option sitting exactly on the readable width still reads fine, so it is left alone.</summary>
    [Fact]
    public void AnOptionExactlyAtTheReadableWidth_ChangesNothing()
    {
        Assert.Equal(OptionButtons_Layout.READABLE_LABEL_WIDTH, TWENTY_EIGHT_CHARACTERS.Length);

        string[] options = [TWENTY_EIGHT_CHARACTERS, "Cancel"];

        var (buttonLabels, optionListText) = OptionButtons_Layout.Build(options);

        Assert.Equal(options, buttonLabels);
        Assert.Null(optionListText);
    }

    /// <summary>And one character past it is the whole reason this class exists.</summary>
    [Fact]
    public void OneCharacterPastTheReadableWidth_TurnsOnTheNumberedList()
    {
        var (buttonLabels, optionListText) = OptionButtons_Layout.Build([TWENTY_NINE_CHARACTERS, "Cancel"]);

        Assert.NotNull(optionListText);
        Assert.StartsWith("1) ", buttonLabels[0], StringComparison.Ordinal);
    }

    // ─────────────────────────── the numbered path ───────────────────────────

    /// <summary>
    /// ALL OR NOTHING. One long option numbers every button, including the short ones. A keyboard
    /// mixing "1) Rebase the branch on…" with a bare "Cancel" reads as two kinds of button and hides
    /// which list line the unnumbered one answers.
    /// </summary>
    [Fact]
    public void OneLongOption_NumbersEveryButtonIncludingTheShortOnes()
    {
        var (buttonLabels, optionListText) = OptionButtons_Layout.Build(["Cancel", A_LONG_OPTION, "Ask me later"]);

        Assert.Equal("1) Cancel", buttonLabels[0]);
        Assert.StartsWith("2) ", buttonLabels[1], StringComparison.Ordinal);
        Assert.Equal("3) Ask me later", buttonLabels[2]);
        Assert.NotNull(optionListText);
    }

    /// <summary>Numbering the owner reads starts at 1, not at the index.</summary>
    [Fact]
    public void TheNumberingStartsAtOne()
    {
        var (buttonLabels, optionListText) = OptionButtons_Layout.Build([A_LONG_OPTION, ANOTHER_LONG_OPTION]);

        Assert.StartsWith("1) ", buttonLabels[0], StringComparison.Ordinal);
        Assert.StartsWith("2) ", buttonLabels[1], StringComparison.Ordinal);
        Assert.StartsWith("1) ", optionListText!, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE PROMISE THE OWNER'S EYES DEPEND ON: nothing that reaches a button is wider than the
    /// button. Twelve options so the prefix grows a second digit — budgeting only the text and
    /// forgetting the "10) " in front of it is exactly how this would regress.
    /// </summary>
    [Fact]
    public void EveryProducedLabel_FitsTheReadableWidth()
    {
        var options = Enumerable.Range(1, 12)
            .Select(number => $"{A_LONG_OPTION} (variant {number})")
            .ToArray();

        var (buttonLabels, _) = OptionButtons_Layout.Build(options);

        foreach (var label in buttonLabels)
            Assert.True(
                Count_TextElements(label) <= OptionButtons_Layout.READABLE_LABEL_WIDTH,
                $"label too wide ({Count_TextElements(label)}): '{label}'");

        Assert.StartsWith("10) ", buttonLabels[9], StringComparison.Ordinal);
    }

    /// <summary>
    /// The ellipsis is INFORMATION: it appears when text was cut and never otherwise, so its absence
    /// tells the owner they are reading the whole option and can tap without opening the list.
    /// </summary>
    [Fact]
    public void TheEllipsisAppearsOnlyWhereTextWasActuallyCut()
    {
        var (buttonLabels, _) = OptionButtons_Layout.Build(["Cancel", A_LONG_OPTION]);

        Assert.DoesNotContain(OptionButtons_Layout.ELLIPSIS, buttonLabels[0], StringComparison.Ordinal);
        Assert.EndsWith(OptionButtons_Layout.ELLIPSIS, buttonLabels[1], StringComparison.Ordinal);

        // One ellipsis, not a trail of them, and none in the middle of the label.
        Assert.Equal(1, buttonLabels[1].Count(character => character.ToString() == OptionButtons_Layout.ELLIPSIS));
    }

    /// <summary>
    /// THE LIST IS THE READABLE COPY. Every option appears there in full, one per line, numbered the
    /// same way the buttons are — a shortened line would defeat the entire manoeuvre.
    /// </summary>
    [Fact]
    public void TheListCarriesEveryOptionInFullOnItsOwnLine()
    {
        string[] options = ["Cancel", A_LONG_OPTION, ANOTHER_LONG_OPTION];

        var (_, optionListText) = OptionButtons_Layout.Build(options);

        var lines = optionListText!.Split('\n');

        Assert.Equal(options.Length, lines.Length);
        Assert.Equal("1) Cancel", lines[0]);
        Assert.Equal($"2) {A_LONG_OPTION}", lines[1]);
        Assert.Equal($"3) {ANOTHER_LONG_OPTION}", lines[2]);

        foreach (var option in options)
            Assert.Contains(option, optionListText, StringComparison.Ordinal);

        Assert.DoesNotContain(OptionButtons_Layout.ELLIPSIS, optionListText, StringComparison.Ordinal);
    }

    // ─────────────────────────── index faithfulness ───────────────────────────

    /// <summary>
    /// The caller maps label[i] back to option[i] BY INDEX. So the count is not a detail: drop or
    /// merge one entry and every later button answers the wrong question — silently, and only on the
    /// owner's phone. Duplicates are kept for the same reason; two identical options are two
    /// separate registrations.
    /// </summary>
    [Fact]
    public void TheOutputCountAlwaysEqualsTheInputCount()
    {
        string[][] inputs =
        [
            ["Yes"],
            ["Yes", "No"],
            ["Yes", "Yes", "Yes"],
            [A_LONG_OPTION, A_LONG_OPTION, "No", "", "   "],
            [.. Enumerable.Range(1, 25).Select(number => $"{A_LONG_OPTION} {number}")],
        ];

        foreach (var options in inputs)
        {
            var (buttonLabels, optionListText) = OptionButtons_Layout.Build(options);

            Assert.Equal(options.Length, buttonLabels.Count);

            if (optionListText is not null)
                Assert.Equal(options.Length, optionListText.Split('\n').Length);
        }
    }

    /// <summary>Order is never touched, and identical options stay two distinguishable buttons.</summary>
    [Fact]
    public void TheOrderIsPreservedAndDuplicatesSurvive()
    {
        var (buttonLabels, optionListText) = OptionButtons_Layout.Build([A_LONG_OPTION, "Same", "Same", ANOTHER_LONG_OPTION]);

        Assert.Equal("2) Same", buttonLabels[1]);
        Assert.Equal("3) Same", buttonLabels[2]);
        Assert.StartsWith("4) Merge it as it stands", buttonLabels[3], StringComparison.Ordinal);
        Assert.Contains($"4) {ANOTHER_LONG_OPTION}", optionListText!, StringComparison.Ordinal);
    }

    // ─────────────────────────── degenerate input ───────────────────────────

    /// <summary>No options means no buttons and no list. It must not throw: this runs on the mirror
    /// path, where a malformed agent entry cannot be allowed to take the bridge down.</summary>
    [Fact]
    public void NoOptions_ProduceNothingAndDoNotThrow()
    {
        var (buttonLabels, optionListText) = OptionButtons_Layout.Build([]);

        Assert.Empty(buttonLabels);
        Assert.Null(optionListText);
    }

    /// <summary>
    /// A blank option keeps its place as the bare number. The agent wrote nothing, so nothing is
    /// invented — but the button must still exist and still carry its number, because the callback
    /// payload behind it belongs to that INDEX. Removing it would shift every later answer.
    /// </summary>
    [Fact]
    public void ABlankOption_StillGetsItsNumberedButton()
    {
        var (buttonLabels, optionListText) = OptionButtons_Layout.Build(["   ", "", A_LONG_OPTION]);

        Assert.Equal("1)", buttonLabels[0]);
        Assert.Equal("2)", buttonLabels[1]);

        var lines = optionListText!.Split('\n');

        Assert.Equal("1)", lines[0]);
        Assert.Equal("2)", lines[1]);
    }

    /// <summary>Blank options among short ones are still the no-change path — only WIDTH numbers.</summary>
    [Fact]
    public void ABlankOptionAmongShortOnes_ChangesNothing()
    {
        string[] options = ["Yes", "   "];

        var (buttonLabels, optionListText) = OptionButtons_Layout.Build(options);

        Assert.Equal(options, buttonLabels);
        Assert.Null(optionListText);
    }

    // ─────────────────────────── characters that bite ───────────────────────────

    /// <summary>
    /// AN EMOJI IS TWO CHARS. `text[..24]` can cut between the halves of a surrogate pair, and what
    /// arrives on the phone is a broken-character box on the button the owner is being asked to tap.
    /// The shortened label must contain whole rockets and nothing else.
    /// </summary>
    [Fact]
    public void ShorteningNeverSplitsAnEmojiInHalf()
    {
        const string ROCKET = "🚀";

        var emojiOption = string.Concat(Enumerable.Repeat(ROCKET, 40));

        var (buttonLabels, optionListText) = OptionButtons_Layout.Build([emojiOption, "Cancel"]);

        var label = buttonLabels[0];

        Assert.EndsWith(OptionButtons_Layout.ELLIPSIS, label, StringComparison.Ordinal);
        Assert.True(
            Count_TextElements(label) <= OptionButtons_Layout.READABLE_LABEL_WIDTH,
            $"label too wide ({Count_TextElements(label)}): '{label}'");

        Assert_NoBrokenCharacters(label);

        // Whole rockets only: strip the "1) " prefix and the ellipsis, and what remains must be an
        // exact number of rockets — half a surrogate pair would leave a stray char behind.
        var emojiPortion = label["1) ".Length..^OptionButtons_Layout.ELLIPSIS.Length];

        Assert.Equal(string.Empty, emojiPortion.Replace(ROCKET, string.Empty));

        // And the list still holds every one of the forty.
        Assert.Contains(emojiOption, optionListText!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The harder case: a family emoji is FOUR emoji stitched with zero-width joiners — eleven chars
    /// that render as one glyph. Cutting by rune rather than by text element would leave a trailing
    /// joiner and a half-family, so the shortening must work in grapheme clusters.
    /// </summary>
    [Fact]
    public void ShorteningNeverSplitsAJoinedEmojiSequence()
    {
        const string FAMILY = "👩‍👩‍👧‍👦";
        const char ZERO_WIDTH_JOINER = '‍';

        var familyOption = string.Concat(Enumerable.Repeat(FAMILY, 30));

        var (buttonLabels, _) = OptionButtons_Layout.Build([familyOption, "Cancel"]);

        var label = buttonLabels[0];

        Assert.EndsWith(OptionButtons_Layout.ELLIPSIS, label, StringComparison.Ordinal);
        Assert_NoBrokenCharacters(label);

        var familyPortion = label["1) ".Length..^OptionButtons_Layout.ELLIPSIS.Length];

        // A joiner INSIDE a family is normal; one left DANGLING at the cut is the bug.
        Assert.NotEqual(ZERO_WIDTH_JOINER, familyPortion[^1]);
        Assert.Equal(string.Empty, familyPortion.Replace(FAMILY, string.Empty));
    }

    /// <summary>An emoji-bearing option that already fits is still left completely alone.</summary>
    [Fact]
    public void AShortEmojiOption_ChangesNothing()
    {
        string[] options = ["🚀 ship it", "🛑 hold"];

        var (buttonLabels, optionListText) = OptionButtons_Layout.Build(options);

        Assert.Equal(options, buttonLabels);
        Assert.Null(optionListText);
    }

    static int Count_TextElements(string text) => new StringInfo(text).LengthInTextElements;

    /// <summary>An independent check for the thing the owner would SEE: an unpaired surrogate.</summary>
    static void Assert_NoBrokenCharacters(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                Assert.True(index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]),
                    $"unpaired high surrogate at {index} in '{text}'");

                index++;
            }
            else
            {
                Assert.False(char.IsLowSurrogate(text[index]),
                    $"unpaired low surrogate at {index} in '{text}'");
            }
        }
    }

    /// <summary>
    /// A FIFTH OPTION FORCES NUMBERING even when every label is short (owner, brief C/E2). Length was
    /// the only trigger before, so five short options rendered as five bare buttons — a column of
    /// five phrases with no numbers, which is exactly the shape the numbered list exists to replace:
    /// the owner has to re-read all of them to find the one they mean, and has no way to answer in
    /// one character.
    ///
    /// FOUR SHORT ONES ARE UNTOUCHED, asserted here beside it, because a rule that numbered
    /// everything would satisfy the first half and quietly change every question the owner sees.
    /// </summary>
    [Fact]
    public void FiveShortOptionsAreNumbered_FourAreLeftAlone()
    {
        var fiveShort = new[] { "yes", "no", "later", "ask Manu", "skip" };

        var five = OptionButtons_Layout.Build(fiveShort);

        Assert.Equal(["1) yes", "2) no", "3) later", "4) ask Manu", "5) skip"], five.ButtonLabels);
        Assert.NotNull(five.OptionListText);

        var fourShort = new[] { "yes", "no", "later", "ask Manu" };

        var four = OptionButtons_Layout.Build(fourShort);

        Assert.Equal(fourShort, four.ButtonLabels);
        Assert.Null(four.OptionListText);
    }

    /// <summary>
    /// The trigger reads the ONE option-count constant rather than a 4 of its own, so the layout and
    /// the coaching cannot come to disagree about what "too many" means — the drift the ceiling in
    /// OwnerMessage_Contract had already suffered.
    /// </summary>
    [Fact]
    public void TheNumberingTriggerIsTheContractsMaximum()
    {
        var atTheMaximum = Enumerable.Range(1, OwnerQuestion_Contract.MAXIMUM_OPTIONS).Select(n => $"opt{n}").ToArray();
        var oneOver = Enumerable.Range(1, OwnerQuestion_Contract.MAXIMUM_OPTIONS + 1).Select(n => $"opt{n}").ToArray();

        Assert.Null(OptionButtons_Layout.Build(atTheMaximum).OptionListText);
        Assert.NotNull(OptionButtons_Layout.Build(oneOver).OptionListText);
    }
}
