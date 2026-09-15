using System.IO;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.TurnCursor;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// A STATE FILE WRITTEN BEFORE THIS FIELD EXISTED MEANS "the dispatcher runs my turns" — that was the
/// file's whole meaning. Reading it as false would silently stop every bridge session on the VPS at
/// the first deploy, which is the one failure this plan must not have.
/// </summary>
public class DrivesTurnsBackCompatTests
{
    [Fact]
    public void A_file_without_the_field_reads_as_driving_turns()
    {
        var file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        // Real PrintSessionState_Store keys are snake_case ("session_id", "orch_id", "sources", …) —
        // verified against PrintSessionState_Store.Read_OrNull / PrintSessionStateStoreTests before
        // writing this fixture. No "drives_turns" key: that is the whole point of this test.
        File.WriteAllText(file, """
        {"session_id":"11111111-1111-1111-1111-111111111111","session_started":true,"role":"implementer",
         "orch_id":"repo-1","member_id":"imp-1","working_directory":"/tmp","channel_file":"/tmp/channel.md",
         "sources":[],"next_turn_number":1,"failed_attempts":0,"executed_turns":[]}
        """);

        var state = PrintSessionState_Store.Read_OrNull(file);

        Assert.NotNull(state);
        Assert.True(state!.DrivesTurns);
        File.Delete(file);
    }

    [Fact]
    public void A_session_that_does_not_drive_turns_round_trips()
    {
        var file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        var written = PrintSessionState_Factory.Create(
            sessionId: "22222222-2222-2222-2222-222222222222", sessionStarted: false,
            role: SessionRoles.Supervisor, orchId: "repo-1", memberId: "supervisor",
            workingDirectory: "/tmp", model: null, channelFilePath: "/tmp/owner-channel.md",
            cursors: new List<ITurnCursor>(), nextTurnNumber: 1, failedAttempts: 0,
            retryNotBeforeUtc: null, executedTurns: [], drivesTurns: false);

        PrintSessionState_Store.Write(file, written);
        var read = PrintSessionState_Store.Read_OrNull(file);

        Assert.NotNull(read);
        Assert.False(read!.DrivesTurns);
        File.Delete(file);
    }
}
