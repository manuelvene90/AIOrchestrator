using System.Text;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

public class CallbackTokenTests
{
    [Theory]
    [InlineData("a1b2c3d4e5f6", 0)]
    [InlineData("a1b2c3d4e5f6", 1)]
    [InlineData("000000000000", 5)]
    [InlineData("ffffffffffff", 42)]
    [InlineData("deadbeefcafe", 1000)]
    public void ABuiltToken_RoundTrips_ThroughParse(string nonce, int optionIndex)
    {
        var data = CallbackToken.Build(nonce, optionIndex);
        var parsed = CallbackToken.Parse_OrNull(data);

        Assert.NotNull(parsed);
        Assert.Equal(nonce, parsed!.Value.Nonce);
        Assert.Equal(optionIndex, parsed.Value.OptionIndex);
    }

    /// <summary>
    /// The nonce is unguessable only if it is actually random and never repeats. Drawing 200 and
    /// checking every one is distinct is a cheap approximation of "never reissued" — a generator
    /// that collided even rarely would let one decision's button answer another's.
    /// </summary>
    [Fact]
    public void NewNonce_IsTwelveLowercaseHexChars_AndNeverRepeatsAcrossManyDraws()
    {
        var nonces = new List<string>();

        for (var i = 0; i < 200; i++)
            nonces.Add(CallbackToken.New_Nonce());

        foreach (var nonce in nonces)
            Assert.Matches("^[0-9a-f]{12}$", nonce);

        Assert.Equal(nonces.Count, nonces.Distinct().Count());
    }

    [Fact]
    public void Build_Throws_ForAnEmptyNonce()
    {
        Assert.Throws<ArgumentException>(() => CallbackToken.Build("", 0));
    }

    [Fact]
    public void Build_Throws_ForAWhitespaceNonce()
    {
        Assert.Throws<ArgumentException>(() => CallbackToken.Build("   ", 0));
    }

    [Fact]
    public void Build_Throws_ForANegativeIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CallbackToken.Build("a1b2c3d4e5f6", -1));
    }

    /// <summary>
    /// Telegram drops the WHOLE keyboard past 64 bytes of callback_data, silently — the question
    /// reaches the phone with nothing to tap. This asserts the byte count directly rather than
    /// trusting the string length, since the payload here happens to be pure ASCII where the two
    /// coincide, and a large index plus a long nonce is the shape most likely to approach the cap.
    /// </summary>
    [Fact]
    public void ALongNonceAndALargeIndex_StillFitUnderTheTelegramCap()
    {
        var nonce = new string('a', 48);
        var data = CallbackToken.Build(nonce, int.MaxValue);

        var byteCount = Encoding.UTF8.GetByteCount(data);

        Assert.True(byteCount <= CallbackToken.TELEGRAM_CALLBACK_DATA_MAX_BYTES);
        Assert.Equal(byteCount, data.Length);
    }

    /// <summary>
    /// The other side of the same constraint: when the arithmetic WOULD exceed 64 bytes, Build must
    /// throw rather than hand back an oversized payload that fails invisibly at Telegram's door.
    /// </summary>
    [Fact]
    public void Build_Throws_RatherThanReturnAnOversizedPayload()
    {
        var nonce = new string('a', 61);

        Assert.Throws<Exception>(() => CallbackToken.Build(nonce, 0));
    }

    [Fact]
    public void ParseOrNull_ReturnsNull_ForNull()
    {
        Assert.Null(CallbackToken.Parse_OrNull(null));
    }

    [Fact]
    public void ParseOrNull_ReturnsNull_ForEmptyString()
    {
        Assert.Null(CallbackToken.Parse_OrNull(""));
    }

    /// <summary>
    /// Other button families ("hold:", "go:", "cmd:show:", "close-yes-"/"close-no-") must fall
    /// through untouched rather than being misread as a malformed option — a tap on one of those
    /// buttons must never be answered as if it were a question option.
    /// </summary>
    [Theory]
    [InlineData("hold:5")]
    [InlineData("go:0")]
    [InlineData("cmd:show:1")]
    [InlineData("close-yes-abc")]
    [InlineData("close-no-abc")]
    public void ParseOrNull_ReturnsNull_ForOtherButtonFamilies(string callbackData)
    {
        Assert.Null(CallbackToken.Parse_OrNull(callbackData));
    }

    [Fact]
    public void ParseOrNull_ReturnsNull_ForAPrefixWithNoSeparator()
    {
        Assert.Null(CallbackToken.Parse_OrNull("opt-"));
    }

    [Fact]
    public void ParseOrNull_ReturnsNull_ForAnEmptyNonceBeforeTheSeparator()
    {
        Assert.Null(CallbackToken.Parse_OrNull("opt-:3"));
    }

    [Fact]
    public void ParseOrNull_ReturnsNull_ForNoIndexAfterTheSeparator()
    {
        Assert.Null(CallbackToken.Parse_OrNull("opt-abc:"));
    }

    [Fact]
    public void ParseOrNull_ReturnsNull_ForANonNumericIndex()
    {
        Assert.Null(CallbackToken.Parse_OrNull("opt-abc:x"));
    }

    /// <summary>
    /// The parser reads the index with NumberStyles.None, so a leading sign must not parse — a
    /// negative or explicitly-positive index string is exactly as invalid as letters would be.
    /// </summary>
    [Fact]
    public void ParseOrNull_ReturnsNull_ForASignedIndex()
    {
        Assert.Null(CallbackToken.Parse_OrNull("opt-abc:-1"));
    }
}
