using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE GATE IS OFF UNTIL SOMEBODY SAYS OTHERWISE. Every machine that states nothing keeps the bash
/// watcher, so this plan can ship in pieces without changing a single live session's behaviour.
/// </summary>
public class WakeModeConfigTests
{
    [Fact]
    public void A_role_that_states_nothing_wakes_on_the_watcher()
    {
        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse("{}") as JsonObject);
        Assert.Equal(WakeModes.Watcher, configs.Get_ForRole(SessionRoles.Supervisor).Wake);
    }

    [Fact]
    public void The_word_ticket_selects_the_ticket()
    {
        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse("""{"runners":{"supervisor":{"runner":"terminal","wake":"ticket"}}}""") as JsonObject);
        Assert.Equal(WakeModes.Ticket, configs.Get_ForRole(SessionRoles.Supervisor).Wake);
        Assert.Equal(WakeModes.Watcher, configs.Get_ForRole(SessionRoles.Implementer).Wake);
    }

    /// <summary>
    /// AND THE REST OF THE NODE IS STILL READ. Asserting only that the mode is Watcher would pass for
    /// two different reasons — the word fell back, or nothing parses `wake` at all — and an assertion
    /// with two routes to its state pins neither (CLAUDE.md decision 20). Pinning `runner` beside it
    /// proves the node WAS parsed and only the unknown word was refused.
    /// </summary>
    [Fact]
    public void An_unknown_word_falls_back_to_the_watcher_without_costing_the_rest_of_the_node()
    {
        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse("""{"runners":{"supervisor":{"runner":"print","resume":"fresh","wake":"telepathy"}}}""") as JsonObject);
        var role = configs.Get_ForRole(SessionRoles.Supervisor);

        Assert.Equal(WakeModes.Watcher, role.Wake);
        Assert.Equal(SessionRunners.Print, role.Runner);
        Assert.Equal(ResumeModes.Fresh, role.Resume);
    }

    /// <summary>
    /// AND IT SURVIVES A SAVE. The whole one-wake-model series ships behind this key, so "reversible in
    /// one edit" is only true if a save does not quietly undo the edit. It did: <c>Write</c> emitted
    /// runner, resume, permission_mode and settings and NOT wake, so a machine set to `ticket` would
    /// have been returned to the watcher by the app's own next save — silently, against this file's own
    /// header promise that "every role and every limit is written".
    /// </summary>
    [Fact]
    public void An_explicit_wake_mode_survives_a_save_and_a_reload()
    {
        var parsed = RunnerConfigs_Json.Parse(JsonNode.Parse("""{"runners":{"supervisor":{"runner":"terminal","wake":"ticket"}}}""") as JsonObject);
        Assert.Equal(WakeModes.Ticket, parsed.Get_ForRole(SessionRoles.Supervisor).Wake);

        var written = new JsonObject();
        RunnerConfigs_Json.Write(written, parsed);
        var reloaded = RunnerConfigs_Json.Parse(written);

        Assert.Equal(WakeModes.Ticket, reloaded.Get_ForRole(SessionRoles.Supervisor).Wake);
        Assert.Equal(WakeModes.Watcher, reloaded.Get_ForRole(SessionRoles.Implementer).Wake);
    }
}
