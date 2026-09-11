using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// The file name arrives inside a Telegram update as `document.file_name` — a string the sending
/// device chose, not a name this machine may trust. These tests hold the sanitizer to the one
/// promise that matters: whatever comes in, what comes out is a bare file name, never a path.
/// </summary>
public class OwnerFileNameSanitizerTests
{
    [Fact]
    public void APlainName_SurvivesUnchanged()
    {
        var result = OwnerFileName_Sanitizer.Sanitize("report.csv", "fallback");

        Assert.Equal("report.csv", result);
    }

    [Fact]
    public void APosixTraversalName_YieldsABareNameWithNoSeparators()
    {
        var result = OwnerFileName_Sanitizer.Sanitize("../../.ssh/authorized_keys", "fallback");

        Assert.Equal("authorized_keys", result);
        Assert.DoesNotContain('/', result);
        Assert.DoesNotContain('\\', result);
    }

    [Fact]
    public void AWindowsStyleTraversalName_YieldsABareNameWithNoSeparators()
    {
        var result = OwnerFileName_Sanitizer.Sanitize("..\\..\\startup\\run.bat", "fallback");

        Assert.Equal("run.bat", result);
        Assert.DoesNotContain('/', result);
        Assert.DoesNotContain('\\', result);
    }

    [Fact]
    public void APosixAbsolutePath_KeepsOnlyTheLastSegment()
    {
        var result = OwnerFileName_Sanitizer.Sanitize("/etc/passwd", "fallback");

        Assert.Equal("passwd", result);
    }

    [Fact]
    public void AWindowsAbsolutePathWithDriveLetter_KeepsOnlyTheLastSegment()
    {
        var result = OwnerFileName_Sanitizer.Sanitize("C:\\keys.txt", "fallback");

        Assert.Equal("keys.txt", result);
    }

    /// <summary>A name that is only dots keeps nothing after the safe-character filter, so the
    /// caller's fallback stem takes over.</summary>
    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("...")]
    public void ANameThatIsOnlyDots_FallsBackToTheSuppliedStem(string dotsOnly)
    {
        var result = OwnerFileName_Sanitizer.Sanitize(dotsOnly, "update-42");

        Assert.Equal("update-42", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ANullEmptyOrWhitespaceName_FallsBackToTheSuppliedStem(string? name)
    {
        var result = OwnerFileName_Sanitizer.Sanitize(name, "update-7");

        Assert.Equal("update-7", result);
    }

    [Fact]
    public void ANameLongerThanMaxLength_IsTruncatedToAtMostMaxLength()
    {
        var longName = new string('a', OwnerFileName_Sanitizer.MAX_LENGTH + 50) + ".txt";

        var result = OwnerFileName_Sanitizer.Sanitize(longName, "fallback");

        Assert.True(result.Length <= OwnerFileName_Sanitizer.MAX_LENGTH);
    }

    [Fact]
    public void CharactersOutsideTheAllowList_AreDropped()
    {
        var result = OwnerFileName_Sanitizer.Sanitize("report#$%(2026)!.csv", "fallback");

        Assert.DoesNotContain('#', result);
        Assert.DoesNotContain('$', result);
        Assert.DoesNotContain('%', result);
        Assert.DoesNotContain('(', result);
        Assert.DoesNotContain(')', result);
        Assert.DoesNotContain('!', result);
    }

    /// <summary>
    /// The whole point of the sanitizer stated as one property, over a table of hostile shapes
    /// including a unicode separator lookalike and a trailing dot — the result must never contain a
    /// character that a filesystem or a later Path.Combine could read as a path separator.
    /// </summary>
    [Theory]
    [InlineData("../../.ssh/authorized_keys")]
    [InlineData("..\\..\\startup\\run.bat")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\keys.txt")]
    [InlineData("a/b/c/d/e.txt")]
    [InlineData("a\\b\\c\\d\\e.txt")]
    [InlineData("weird\u2215name.txt")]
    [InlineData("trailing.dot.")]
    [InlineData("::::")]
    [InlineData("a:b:c")]
    public void TheResultNeverContainsASeparatorCharacter(string hostileName)
    {
        var result = OwnerFileName_Sanitizer.Sanitize(hostileName, "fallback-stem");

        Assert.DoesNotContain('/', result);
        Assert.DoesNotContain('\\', result);
        Assert.DoesNotContain(':', result);
    }

    /// <summary>The fallback stem passes through the same safe-character filter as the sender's
    /// own name — a caller that composes it carelessly must not get an unsanitised result back.</summary>
    [Fact]
    public void TheFallbackStemItselfIsSanitised()
    {
        var result = OwnerFileName_Sanitizer.Sanitize(null, "../weird/fallback#name");

        Assert.DoesNotContain('/', result);
        Assert.DoesNotContain('\\', result);
        Assert.DoesNotContain('#', result);
    }
}
