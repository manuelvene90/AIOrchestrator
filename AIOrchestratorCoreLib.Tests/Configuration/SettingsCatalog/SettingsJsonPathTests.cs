using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsCatalog;

/// <summary>
/// ONE WALK OVER THE RAW TREE, because the alternative is a parser per block. The fork's loader
/// already keeps the JsonObject for Save (spec §6.1: "New scalar keys are read by one generic
/// SettingsReader that walks the raw JsonObject"), so every catalogue key — flat like
/// `telegramInbound`, nested like `phone.status.periodic`, three deep like `runners.supervisor.runner`
/// — is the same operation on the same object.
/// </summary>
public class SettingsJsonPathTests
{
    static JsonObject Tree() => (JsonObject)JsonNode.Parse("""
        {
          "telegramInbound": "poll",
          "phone": { "push": "filtered", "status": { "periodic": true, "intervalMinutes": 30 } },
          "runners": { "supervisor": { "runner": "stream" } }
        }
        """)!;

    [Fact]
    public void Read_AFlatKey_ReturnsItsValue()
    {
        Assert.Equal("poll", SettingsJson_Path.Read_OrNull(Tree(), "telegramInbound")!.GetValue<string>());
    }

    [Fact]
    public void Read_AThreeSegmentPath_WalksEveryObject()
    {
        Assert.True(SettingsJson_Path.Read_OrNull(Tree(), "phone.status.periodic")!.GetValue<bool>());
        Assert.Equal("stream", SettingsJson_Path.Read_OrNull(Tree(), "runners.supervisor.runner")!.GetValue<string>());
    }

    /// <summary>
    /// AN ABSENT KEY AND A NULL ROOT ARE THE SAME ANSWER, and that is what makes the resolver's
    /// layers composable: "this layer says nothing" must not need a try/catch at four call sites.
    /// </summary>
    [Fact]
    public void Read_AnAbsentKey_AnAbsentParent_OrANullRoot_AllReadAsAbsent()
    {
        Assert.Null(SettingsJson_Path.Read_OrNull(Tree(), "phone.receipts"));
        Assert.Null(SettingsJson_Path.Read_OrNull(Tree(), "pulse.fields"));
        Assert.Null(SettingsJson_Path.Read_OrNull(Tree(), "pulse.holdToggle.deeper"));
        Assert.Null(SettingsJson_Path.Read_OrNull(null, "telegramInbound"));
    }

    /// <summary>
    /// A SEGMENT THAT IS NOT AN OBJECT STOPS THE WALK RATHER THAN THROWING. `{"phone": 3}` is a
    /// plausible hand-edit, and the loader's own readers were made tolerant for exactly this reason
    /// on 2026-09-10: a typo costs that ONE setting its default, never the app's startup.
    /// </summary>
    [Fact]
    public void Read_ThroughAScalarSegment_IsAbsent_NotAThrow()
    {
        var tree = (JsonObject)JsonNode.Parse("""{"phone": 3}""")!;

        Assert.Null(Record.Exception(() => SettingsJson_Path.Read_OrNull(tree, "phone.push")));
        Assert.Null(SettingsJson_Path.Read_OrNull(tree, "phone.push"));
    }

    [Fact]
    public void Write_CreatesEveryMissingParent_AndLeavesSiblingsAlone()
    {
        var tree = Tree();

        SettingsJson_Path.Write(tree, "pulse.holdToggle", JsonValue.Create(false));
        SettingsJson_Path.Write(tree, "phone.receipts", JsonValue.Create("reactions"));

        Assert.False(SettingsJson_Path.Read_OrNull(tree, "pulse.holdToggle")!.GetValue<bool>());
        Assert.Equal("reactions", SettingsJson_Path.Read_OrNull(tree, "phone.receipts")!.GetValue<string>());
        Assert.Equal("filtered", SettingsJson_Path.Read_OrNull(tree, "phone.push")!.GetValue<string>());
        Assert.True(SettingsJson_Path.Read_OrNull(tree, "phone.status.periodic")!.GetValue<bool>());
    }

    /// <summary>
    /// RESET DELETES THE KEY, it does not write the default — the rule of spec §6.2, and the same
    /// rule the loader already keeps for reviewerModel: a materialised default is a default that can
    /// never move again, frozen on the first button press.
    /// </summary>
    [Fact]
    public void Remove_DeletesTheKey_AndReportsWhetherThereWasOne()
    {
        var tree = Tree();

        Assert.True(SettingsJson_Path.Remove(tree, "phone.status.intervalMinutes"));
        Assert.Null(SettingsJson_Path.Read_OrNull(tree, "phone.status.intervalMinutes"));
        Assert.True(SettingsJson_Path.Read_OrNull(tree, "phone.status.periodic")!.GetValue<bool>());
        Assert.False(SettingsJson_Path.Remove(tree, "phone.status.intervalMinutes"));
        Assert.False(SettingsJson_Path.Remove(tree, "nothing.here"));
    }
}
