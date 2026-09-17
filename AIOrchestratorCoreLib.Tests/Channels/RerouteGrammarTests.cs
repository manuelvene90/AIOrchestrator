using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// THE TWO WORDS OF A RE-REVIEW CONTRACT, in the one place a marker word exists. `REROUTE:` is the
/// SUPERVISOR's — "when imp-1 reports a fix, the app relays it to rev-1 instead of waking me" — and
/// `FIXED:` is the IMPLEMENTER's answer, the commit that closes the round. One word per author, and
/// neither nested inside the other: a supervisor line carrying the word FIXED would be a declaration
/// of a marker by the wrong session, which is the distinction Contains_Marker exists to keep.
/// </summary>
public class RerouteGrammarTests
{
    [Fact]
    public void BothWordsAreInTheGrammarAndCarryTheirColon()
    {
        Assert.Equal("REROUTE:", ChannelGrammar.REROUTE);
        Assert.Equal("FIXED:", ChannelGrammar.FIXED);
    }

    /// <summary>
    /// AND THE GRAMMAR'S OWN LIST KNOWS THEM. All_Markers is what RoleCommandMarkerTests walks to
    /// decide whether a phrase a skill teaches is real vocabulary; a marker added as a property but
    /// not to the data file would be invisible to it, and to `channel-append.sh` as well.
    /// </summary>
    [Fact]
    public void BothWordsAreInTheGrammarsOwnMarkerList()
    {
        Assert.Contains(ChannelGrammar.REROUTE, ChannelGrammar.All_Markers);
        Assert.Contains(ChannelGrammar.FIXED, ChannelGrammar.All_Markers);
    }

    /// <summary>
    /// AND THEY COME FROM THE DATA FILE, NOT FROM A C# CONSTANT. `All_Markers` is read out of the
    /// embedded JSON, so a property returning a literal would pass the case above and still leave
    /// `channel-append.sh` — which reads the same file with jq — spelling nothing. Asserted through
    /// the grammar's own key lookup, which throws for a key the JSON does not carry.
    /// </summary>
    [Fact]
    public void BothWordsAreReadByTheirGrammarKey()
    {
        Assert.Equal(ChannelGrammar.REROUTE, ChannelGrammar.Marker("reroute"));
        Assert.Equal(ChannelGrammar.FIXED, ChannelGrammar.Marker("fixed"));
    }
}
