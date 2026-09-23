using AIOrchestratorCoreLib.Bridge.SuppressedEntries;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.SuppressedEntries;

/// <summary>
/// The drained entries become ONE message at turn end — "the completion" the owner asked for on
/// 2026-08-25 (*"the terminal completes the operation and stops, and I haven't received anything
/// telling me 'done'"*), in the order the session said them.
/// </summary>
public class SuppressedDigestBuilderTests
{
    [Fact]
    public void Build_OfNothing_IsNull()
    {
        Assert.Null(SuppressedDigest_Builder.Build_OrNull([]));
    }

    [Fact]
    public void Build_JoinsTheTexts_InOrder_WithABlankLineBetween()
    {
        var digest = SuppressedDigest_Builder.Build_OrNull([("a", "🔴 Sup: first"), ("b", "🔴 Sup: second")]);

        Assert.Equal("🔴 Sup: first\n\n🔴 Sup: second", digest);
    }

    /// <summary>An entry with nothing to read is skipped, not rendered as a blank paragraph; with nothing left there is no digest.</summary>
    [Fact]
    public void Build_SkipsBlankTexts_AndIsNullWhenOnlyBlanksRemain()
    {
        Assert.Equal("🔴 Sup: kept", SuppressedDigest_Builder.Build_OrNull([("a", "  "), ("b", "🔴 Sup: kept")]));
        Assert.Null(SuppressedDigest_Builder.Build_OrNull([("a", ""), (null, "\n")]));
    }
}
