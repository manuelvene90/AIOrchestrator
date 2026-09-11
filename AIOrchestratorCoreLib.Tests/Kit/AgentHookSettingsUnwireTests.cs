using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Kit;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// The removal half of the wirer — what a machine upgrading to the plugin depends on. Left wired,
/// the ledger hook fires TWICE for a supervisor (once from settings.json, once from the role's own
/// frontmatter) and once for every unrelated session on the machine, which is the failure this
/// stage exists to end.
/// </summary>
public class AgentHookSettingsUnwireTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), $"aiorch-unwire-{Guid.NewGuid():N}");

    public AgentHookSettingsUnwireTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    string SettingsFile => Path.Combine(_folder, "settings.json");
    string HookFile => Path.Combine(_folder, "hooks", "supervisor-ledger-check.sh");

    [Fact]
    public void AWiredHook_IsRemoved_AndTheEventItEmptiedGoesWithIt()
    {
        AgentHookSettings_Wirer.Ensure_Wired(SettingsFile, HookFile, AgentHookSettings_Wirer.STOP_EVENT, null);

        Assert.Equal(UnwireOutcomes.Unwired, AgentHookSettings_Wirer.Ensure_Unwired(SettingsFile, HookFile));

        var root = JsonNode.Parse(File.ReadAllText(SettingsFile)) as JsonObject;
        Assert.Null(root!["hooks"]?["Stop"]);
    }

    /// <summary>
    /// ONLY AN EVENT WE EMPTIED. The cleanup used to run over every event in the file, so a user who
    /// keeps `"SessionStart": []` had that key deleted and settings.json rewritten on a run that
    /// removed nothing of ours — and the caller then logged, seven times, that it had un-wired hooks
    /// the file never contained.
    /// </summary>
    [Fact]
    public void AnEmptyEventThatIsNotOurs_IsLeftExactlyWhereItWas()
    {
        File.WriteAllText(SettingsFile, """{ "hooks": { "SessionStart": [] } }""");

        var outcome = AgentHookSettings_Wirer.Ensure_Unwired(SettingsFile, HookFile);

        Assert.Equal(UnwireOutcomes.NothingToDo, outcome);

        var root = JsonNode.Parse(File.ReadAllText(SettingsFile)) as JsonObject;
        Assert.NotNull(root!["hooks"]?["SessionStart"]);
    }

    [Fact]
    public void SomebodyElsesHookOnTheSameEvent_SurvivesAndKeepsItsEvent()
    {
        AgentHookSettings_Wirer.Ensure_Wired(SettingsFile, HookFile, AgentHookSettings_Wirer.STOP_EVENT, null);
        AgentHookSettings_Wirer.Ensure_Wired(SettingsFile, Path.Combine(_folder, "hooks", "their-own.sh"), AgentHookSettings_Wirer.STOP_EVENT, null);

        AgentHookSettings_Wirer.Ensure_Unwired(SettingsFile, HookFile);

        var stop = JsonNode.Parse(File.ReadAllText(SettingsFile))!["hooks"]!["Stop"] as JsonArray;
        Assert.Single(stop!);
        Assert.Contains("their-own.sh", stop!.ToJsonString());
    }

    /// <summary>
    /// A file that is present and unparseable is NOT "nothing to do": our entries, if any, are still
    /// live and nobody has been told. It used to return false, indistinguishable from a clean machine.
    /// </summary>
    [Fact]
    public void AnUnparseableSettingsFile_SaysItCouldNotTell_RatherThanNothingToDo()
    {
        File.WriteAllText(SettingsFile, "{ not json at all");

        Assert.Equal(UnwireOutcomes.Unreadable, AgentHookSettings_Wirer.Ensure_Unwired(SettingsFile, HookFile));
    }

    [Fact]
    public void NoSettingsFileAtAll_IsNothingToDo()
    {
        Assert.Equal(UnwireOutcomes.NothingToDo, AgentHookSettings_Wirer.Ensure_Unwired(SettingsFile, HookFile));
    }
}
