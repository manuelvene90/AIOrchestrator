using System.Text;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Spawning;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Usage;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Spawning;

/// <summary>
/// The resolver answers ONE question before a respawn: does this slot have a conversation of its own
/// that `claude --resume` can pick up? The answer comes from the slot's probe file — the statusline
/// dumps its raw payload there, `session_id` and `transcript_path` included — and it is YES only
/// when the transcript the CLI itself named still exists. `--resume` on an id with no transcript
/// prints "No conversation found with session ID" and exits (verified against Claude Code 2.1.267),
/// which under the watchdog would be a respawn loop, so the resolver refuses to name such an id.
/// </summary>
public class ResumableSessionResolverTests : IDisposable
{
    const string ORCH_ID = "arb-fix";
    const string SOLO_ID = "solo-1";
    const string SESSION_ID = "504fb5ee-d79d-410e-9876-8fb937949dfe";

    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public ResumableSessionResolverTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-resume-tests-{Guid.NewGuid():N}");
        _paths = SupervisionPaths_Factory.Create(_tempRoot);

        Directory.CreateDirectory(_paths.Get_OrchestrationFolder(ORCH_ID));
        Directory.CreateDirectory(_paths.Get_ImplementerFolder(ORCH_ID, SOLO_ID));
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Resolve_ForSupervisor_NamesTheIdTheProbeFileCarries_WhenItsTranscriptExists()
    {
        var transcript = Write_Transcript(SESSION_ID);
        Write_ProbeFile(Supervisor_ProbeFile(), SESSION_ID, transcript);

        Assert.Equal(SESSION_ID, ResumableSession_Resolver.Resolve_ForSupervisor_OrNull(_paths, ORCH_ID));
    }

    /// <summary>A solo's probe lives in ITS member folder, not the orchestration's — the supervisor slot.</summary>
    [Fact]
    public void Resolve_ForMember_ReadsTheMemberSlotsOwnProbeFile()
    {
        var transcript = Write_Transcript(SESSION_ID);
        Write_ProbeFile(Member_ProbeFile(SOLO_ID), SESSION_ID, transcript);

        Assert.Equal(SESSION_ID, ResumableSession_Resolver.Resolve_ForMember_OrNull(_paths, ORCH_ID, SOLO_ID));
        Assert.Null(ResumableSession_Resolver.Resolve_ForSupervisor_OrNull(_paths, ORCH_ID));
    }

    /// <summary>The FIRST spawn: the statusline has never rendered, so there is no probe file and nothing to resume.</summary>
    [Fact]
    public void Resolve_IsNull_WhenTheSlotHasNoProbeFileYet()
    {
        Assert.Null(ResumableSession_Resolver.Resolve_ForSupervisor_OrNull(_paths, ORCH_ID));
        Assert.Null(ResumableSession_Resolver.Resolve_ForMember_OrNull(_paths, ORCH_ID, SOLO_ID));
    }

    /// <summary>
    /// The id alone is not enough: `claude --resume` of an id whose transcript is gone prints
    /// "No conversation found" and exits, and the watchdog would respawn it into the same wall.
    /// </summary>
    [Fact]
    public void Resolve_IsNull_WhenTheTranscriptTheProbeNamesIsGone()
    {
        var transcript = Path.Combine(_tempRoot, "projects", $"{SESSION_ID}.jsonl");
        Write_ProbeFile(Supervisor_ProbeFile(), SESSION_ID, transcript);

        Assert.False(File.Exists(transcript));
        Assert.Null(ResumableSession_Resolver.Resolve_ForSupervisor_OrNull(_paths, ORCH_ID));
    }

    [Fact]
    public void Resolve_IsNull_WhenTheTranscriptIsEmpty()
    {
        var transcript = Path.Combine(_tempRoot, "projects", $"{SESSION_ID}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(transcript) ?? throw new Exception($"Transcript path '{transcript}' has no directory"));
        File.WriteAllText(transcript, string.Empty);
        Write_ProbeFile(Supervisor_ProbeFile(), SESSION_ID, transcript);

        Assert.Null(ResumableSession_Resolver.Resolve_ForSupervisor_OrNull(_paths, ORCH_ID));
    }

    /// <summary>
    /// The id travels through a PowerShell command line, so only the CLI's own id shape (a UUID in
    /// 8-4-4-4-12 form) is ever handed on. A probe file is written by a process the app does not
    /// control; anything else in that field is refused, never quoted into a shell.
    /// </summary>
    [Fact]
    public void Resolve_IsNull_WhenTheIdIsNotAUuid()
    {
        var transcript = Write_Transcript(SESSION_ID);

        Write_ProbeFile(Supervisor_ProbeFile(), "7f34ac2b", transcript);
        Assert.Null(ResumableSession_Resolver.Resolve_ForSupervisor_OrNull(_paths, ORCH_ID));

        Write_ProbeFile(Supervisor_ProbeFile(), "abc'; Remove-Item -Recurse C:\\", transcript);
        Assert.Null(ResumableSession_Resolver.Resolve_ForSupervisor_OrNull(_paths, ORCH_ID));
    }

    /// <summary>
    /// Every probe file on disk starts with a BOM — `Set-Content -Encoding utf8` on Windows
    /// PowerShell 5.1 writes one — and JSON parsing of a BOM-prefixed string throws. The resolver
    /// reads through the shared file reader that strips it, like every other probe reader.
    /// </summary>
    [Fact]
    public void Resolve_ReadsThroughTheBomEveryProbeFileCarries()
    {
        var transcript = Write_Transcript(SESSION_ID);
        Write_ProbeFile(Supervisor_ProbeFile(), SESSION_ID, transcript, withBom: true);

        var bytes = File.ReadAllBytes(Supervisor_ProbeFile());
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());

        Assert.Equal(SESSION_ID, ResumableSession_Resolver.Resolve_ForSupervisor_OrNull(_paths, ORCH_ID));
    }

    /// <summary>A half-written probe (the statusline rewrites it every render) is unknown, never a throw in the respawn path.</summary>
    [Fact]
    public void Resolve_IsNull_ForAMalformedProbeFile_NeverThrows()
    {
        File.WriteAllText(Supervisor_ProbeFile(), """{"session_id":"504fb5ee-d79d-410e-9876-8fb937949dfe","transcript_pa""");
        Assert.Null(ResumableSession_Resolver.Resolve_ForSupervisor_OrNull(_paths, ORCH_ID));

        File.WriteAllText(Supervisor_ProbeFile(), string.Empty);
        Assert.Null(ResumableSession_Resolver.Resolve_ForSupervisor_OrNull(_paths, ORCH_ID));

        File.WriteAllText(Supervisor_ProbeFile(), """{"session_id":"504fb5ee-d79d-410e-9876-8fb937949dfe"}""");
        Assert.Null(ResumableSession_Resolver.Resolve_ForSupervisor_OrNull(_paths, ORCH_ID));
    }

    string Supervisor_ProbeFile()
    {
        return Path.Combine(_paths.Get_OrchestrationFolder(ORCH_ID), UsageTotals_Reader.SESSION_USAGE_FILE);
    }

    string Member_ProbeFile(string memberId)
    {
        return Path.Combine(_paths.Get_ImplementerFolder(ORCH_ID, memberId), UsageTotals_Reader.SESSION_USAGE_FILE);
    }

    string Write_Transcript(string sessionId)
    {
        var transcript = Path.Combine(_tempRoot, "projects", $"{sessionId}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(transcript) ?? throw new Exception($"Transcript path '{transcript}' has no directory"));
        File.WriteAllText(transcript, """{"type":"summary","summary":"a conversation"}""" + "\n");
        return transcript;
    }

    /// <summary>The live probe shape, trimmed to what the resolver reads — written the way the statusline writes it.</summary>
    static void Write_ProbeFile(string probeFile, string sessionId, string transcriptPath, bool withBom = true)
    {
        var payload = new JsonObject
        {
            ["session_id"] = sessionId,
            ["transcript_path"] = transcriptPath,
            ["model"] = new JsonObject { ["id"] = "claude-fable-5-1", ["display_name"] = "Fable 5.1" },
            ["version"] = "2.1.267",
        };

        File.WriteAllText(probeFile, payload.ToJsonString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: withBom));
    }
}
