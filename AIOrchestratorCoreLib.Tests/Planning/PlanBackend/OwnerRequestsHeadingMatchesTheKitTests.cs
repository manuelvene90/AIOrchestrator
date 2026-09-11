using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanBackend;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning.PlanBackend;

/// <summary>
/// THE APP NOW WRITES A SECTION THE ROLE COMMANDS TEACH, so there are three copies of its heading and
/// its table shape: `kit/skills/supervisor/SKILL.md`, `kit/skills/solo/SKILL.md`, and
/// <see cref="PlanRequest_Writer"/>. That is the drift mechanism `PlanLedger_Markers` documents — a
/// marker list written down five times ended up with four entries in two of them — arriving in a new
/// place, and prose cannot hold it.
///
/// The join is cross-language: the role commands are markdown a session reads, and nothing can import
/// them. Reading the files is the only assertion available, and it FAILS LOUDLY when it cannot find
/// them: a harness that cannot find what it tests must refuse to run rather than certify the absence
/// of the thing it is testing.
/// </summary>
public class OwnerRequestsHeadingMatchesTheKitTests
{
    [Theory]
    [InlineData("supervisor.md")]
    [InlineData("solo.md")]
    public void TheHeadingTheAppWritesIsTheOneTheRoleCommandTeaches(string command)
    {
        var text = Read_Command(command);

        Assert.Contains(PlanRequest_Writer.OWNER_REQUESTS_HEADING, text);
    }

    [Theory]
    [InlineData("supervisor.md")]
    [InlineData("solo.md")]
    public void TheTableShapeTheAppWritesIsTheOneTheRoleCommandTeaches(string command)
    {
        var text = Read_Command(command);

        Assert.Contains(PlanRequest_Writer.TABLE_HEADER_ROW, text);
        Assert.Contains(PlanRequest_Writer.TABLE_SEPARATOR_ROW, text);
    }

    /// <summary>
    /// And the section the app writes into is the one the PARSER skips — otherwise every ingested row
    /// would land in the owner's denominator twice, once as a table row and once as the ledger line
    /// beside it.
    /// </summary>
    [Fact]
    public void TheHeadingOpensASectionTheBarCannotSee()
    {
        Assert.True(PlanLedger_Sections.Opens_NonLedgerSection(PlanRequest_Writer.OWNER_REQUESTS_HEADING));

        Assert.StartsWith(
            PlanLedger_Sections.OWNER_REQUESTS_HEADING_PREFIX,
            PlanLedger_Sections.Read_HeadingTitle_OrNull(PlanRequest_Writer.OWNER_REQUESTS_HEADING),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The role protocols moved to kit/skills/&lt;role&gt;/SKILL.md when the kit became a plugin. The
    /// argument is still the old "&lt;role&gt;.md" spelling, because it is what every assertion above
    /// reads as; only where the file LIVES changed. Refuses rather than returning a guess — a
    /// content test that located no content passes by finding nothing (decision 20).
    /// </summary>
    static string Read_Command(string fileName)
    {
        var role = Path.GetFileNameWithoutExtension(fileName);

        return Kit.KitRepoFiles.Find_RoleProtocol(role) is string path
            ? File.ReadAllText(path)
            : throw new Exception($"kit/skills/{role}/SKILL.md was not found walking up from {AppContext.BaseDirectory} — REFUSING to assert about a file this harness never read.");
    }
}
