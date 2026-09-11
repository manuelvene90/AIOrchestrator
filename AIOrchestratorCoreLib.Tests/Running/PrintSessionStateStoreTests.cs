using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.ExecutedTurn;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.TurnCursor;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

public class PrintSessionStateStoreTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public PrintSessionStateStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-print-state-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void WriteThenRead_RoundTripsEverything()
    {
        var file = PrintSessionState_Store.Get_StateFile(_paths, SessionRoles.Implementer, "orch-1", "imp-1");
        var executed = ExecutedTurn_Factory.Create(1, "orch-1/imp-1/1", 2, 3, new DateTime(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc), "success", 0.0127, "stage-sid-1");
        var cursor = TurnCursor_Factory.Create("imp-1", "/repo/ch.md", 3, new HashSet<string> { "0f1e2d3c4b5a6978", "aabbccddeeff0011" });
        var state = PrintSessionState_Factory.CreateFrom_Existing_TurnExecuted(
            PrintSessionState_Factory.Create_New("sid", SessionRoles.Implementer, "orch-1", "imp-1", "/repo", "opus", "/repo/ch.md", []), executed, "sid", [cursor]);

        PrintSessionState_Store.Write(file, state);
        var read = PrintSessionState_Store.Read_OrNull(file);

        Assert.NotNull(read);
        Assert.Equal("sid", read.SessionId);
        Assert.Equal(SessionRoles.Implementer, read.Role);
        Assert.Equal("opus", read.Model);
        Assert.Equal("imp-1", Assert.Single(read.Cursors).SourceKey);
        Assert.Equal("/repo/ch.md", read.Cursors[0].ChannelFilePath);
        Assert.Equal(3, read.Cursors[0].HighWaterIndex);
        Assert.Equal(["0f1e2d3c4b5a6978", "aabbccddeeff0011"], read.Cursors[0].Delivered.OrderBy(identity => identity, StringComparer.Ordinal));
        Assert.Equal(2, read.NextTurnNumber);
        Assert.Equal(0, read.FailedAttempts);
        Assert.Single(read.ExecutedTurns);
        Assert.Equal("orch-1/imp-1/1", read.ExecutedTurns[0].RequestId);
        Assert.Equal(new DateTime(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc), read.ExecutedTurns[0].EndedUtc);
        Assert.Equal(0.0127, read.ExecutedTurns[0].CostUsd);
        // The stage's own session id — the attribution key for a fresh-mode transcript (2026-09-09).
        Assert.Equal("stage-sid-1", read.ExecutedTurns[0].SessionId);
        Assert.True(PrintSessionState_Store.Exists(_paths, SessionRoles.Implementer, "orch-1", "imp-1"));
    }

    [Fact]
    public void ExecutedTurn_WrittenBeforeSessionIdExisted_ReadsAsNull()
    {
        var file = PrintSessionState_Store.Get_StateFile(_paths, SessionRoles.Implementer, "orch-1", "imp-1");
        var executed = ExecutedTurn_Factory.Create(1, "orch-1/imp-1/1", 2, 3, DateTime.UtcNow, "success", null);
        var state = PrintSessionState_Factory.CreateFrom_Existing_TurnExecuted(
            PrintSessionState_Factory.Create_New("sid", SessionRoles.Implementer, "orch-1", "imp-1", "/repo", null, "/repo/ch.md", []), executed, "sid", []);
        PrintSessionState_Store.Write(file, state);

        // Exactly what a file from before 2026-09-09 looks like: no session_id on the turn.
        var text = File.ReadAllText(file).Replace("\"session_id\": null,", string.Empty).Replace("\"session_id\":null,", string.Empty);
        File.WriteAllText(file, text);

        var read = PrintSessionState_Store.Read_OrNull(file);

        Assert.NotNull(read);
        Assert.Null(Assert.Single(read.ExecutedTurns).SessionId);
    }

    [Fact]
    public void AbsentFile_IsNull_AndNotRegistered()
    {
        Assert.Null(PrintSessionState_Store.Read_OrNull(Path.Combine(_tempRoot, "nope.json")));
        Assert.False(PrintSessionState_Store.Exists(_paths, SessionRoles.Solo, "orch-1", "solo-1"));
    }

    [Fact]
    public void OptionalFields_MayBeMissing()
    {
        var file = Path.Combine(_tempRoot, "minimal.json");
        File.WriteAllText(file, """{"session_id":"s","role":"general","orch_id":"general","member_id":"general","working_directory":"/g","channel_file":"/g/channel.md"}""");

        var read = PrintSessionState_Store.Read_OrNull(file);

        Assert.NotNull(read);
        Assert.Empty(read.Cursors);
        Assert.Equal(1, read.NextTurnNumber);
        Assert.Empty(read.ExecutedTurns);
    }

    [Fact]
    public void StateFile_AndChannel_LiveWhereEachRoleReads()
    {
        Assert.Equal(Path.Combine(_paths.Get_ImplementerFolder("o", "rev-1"), PrintSessionState_Store.STATE_FILE_NAME), PrintSessionState_Store.Get_StateFile(_paths, SessionRoles.Reviewer, "o", "rev-1"));
        Assert.Equal(Path.Combine(_paths.GeneralFolder, PrintSessionState_Store.STATE_FILE_NAME), PrintSessionState_Store.Get_StateFile(_paths, SessionRoles.General, ChannelDiscovery.GENERAL_ORCH_ID, "general"));
        Assert.Equal(_paths.Get_ImplementerChannelFile("o", "imp-1"), TurnSources_Resolver.Resolve_Own(_paths, SessionRoles.Implementer, "o", "imp-1").ChannelFilePath);
        Assert.Equal(_paths.Get_OwnerChannelFile("o"), TurnSources_Resolver.Resolve_Own(_paths, SessionRoles.Solo, "o", "solo-1").ChannelFilePath);
        Assert.Equal(_paths.GeneralChannelFile, TurnSources_Resolver.Resolve_Own(_paths, SessionRoles.General, ChannelDiscovery.GENERAL_ORCH_ID, "general").ChannelFilePath);
    }

    [Fact]
    public void Transitions_MoveOnlyWhatTheyName()
    {
        var fresh = PrintSessionState_Factory.Create_New("sid", SessionRoles.Solo, "o", "solo-1", "/r", null, "/r/c.md", []);

        var failed = PrintSessionState_Factory.CreateFrom_Existing_AttemptFailed(fresh);
        Assert.Equal(1, failed.FailedAttempts);
        Assert.Equal(1, failed.NextTurnNumber);

        var reset = PrintSessionState_Factory.CreateFrom_Existing_AttemptsReset(failed);
        Assert.Equal(0, reset.FailedAttempts);

        var skipped = PrintSessionState_Factory.CreateFrom_Existing_TurnSkipped(failed);
        Assert.Equal(2, skipped.NextTurnNumber);
        Assert.Equal(0, skipped.FailedAttempts);
    }
}
