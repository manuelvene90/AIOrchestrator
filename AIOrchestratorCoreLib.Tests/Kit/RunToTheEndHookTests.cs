using System.Diagnostics;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// THE HOOK THAT STOPS A SESSION STOPPING.
///
/// The owner told sessions to run an endeavour to the end, repeatedly, and they kept stopping —
/// *"no matter how many times i tell it not to get stuck and keep going, it will keep getting stuck,
/// so I fear the solution we implemented might not be enough"* (2026-08-20). They were right: the
/// solution was prose in a role command, and prose is exactly what had already failed.
///
/// The ledger hook next door had already written the principle down: *"The difference is
/// enforcement, not diligence."* This exercises the enforcement rather than reading it — the bash
/// half cannot be reached from C# any other way, and the ONE bug this hook shipped with (a `grep -c`
/// that prints 0 and exits 1, so `|| echo 0` produced "0 0" and a finished ledger blocked) was found
/// by running it, never by reading it.
///
/// Nothing here skips. If bash is missing the test FAILS: a harness that cannot run what it tests
/// must not certify it.
/// </summary>
public class RunToTheEndHookTests : IDisposable
{
    readonly string _home;
    readonly string _orch;

    public RunToTheEndHookTests()
    {
        _home = Path.Combine(Path.GetTempPath(), $"aiorch-runtoend-{Guid.NewGuid():N}");
        _orch = Path.Combine(_home, ".claude", "supervision", "test-1");
        Directory.CreateDirectory(_orch);
        Write_Channel("## [1] FROM solo - x - a report\nall good\n");
    }

    public void Dispose() => Directory.Delete(_home, recursive: true);

    /// <summary>The whole point: open work, nobody waiting on the owner, so the turn may not end.</summary>
    [Fact]
    public void OpenWorkWithNothingBlockedMeansTheTurnCannotEnd()
    {
        Write_Plan("- [x] shipped\n- [ ] still to do\n");

        Assert.True(Blocks("solo"), "a session with open ledger work was allowed to stop — the exact failure this hook exists to prevent");
        Assert.True(Blocks("supervisor"));
    }

    /// <summary>
    /// THE EXIT THAT MAKES THE RULE LIVABLE. It is not "never stop", it is "never stop with work
    /// still open" — without this a finished orchestration could never end a turn again.
    /// </summary>
    [Fact]
    public void AFinishedLedgerLetsTheTurnEnd()
    {
        Write_Plan("- [x] shipped\n- [x] also shipped\n- [-] dropped, superseded\n");

        Assert.False(Blocks("solo"));
    }

    /// <summary>`- [?]` says something truly waits on the owner. That is blocked, not stalling.</summary>
    [Fact]
    public void ALineBlockedOnTheOwnerLetsTheTurnEnd()
    {
        Write_Plan("- [ ] still to do\n- [?] the schema - blocked on: their decision\n");

        Assert.False(Blocks("solo"));
    }

    /// <summary>
    /// ONLY THE LAST ENTRY COUNTS. An old question further up the channel was answered long ago, and
    /// honouring it would let one ancient QUESTION exempt every turn for the rest of the orchestration.
    /// </summary>
    [Fact]
    public void OnlyAQuestionInTheLASTEntryLetsTheTurnEnd()
    {
        Write_Plan("- [ ] still to do\n");

        Write_Channel("## [1] FROM solo - x - asked\nQUESTION: which way?\nOPTION: a\n\n## [2] FROM solo - x - later\njust a report\n");
        Assert.True(Blocks("solo"), "an OLD question exempted the turn — that would hold for the rest of the orchestration");

        Write_Channel("## [1] FROM solo - x - asking\nbody\nQUESTION: which way?\nOPTION: a\nOPTION: b\n");
        Assert.False(Blocks("solo"));
    }

    /// <summary>
    /// Members are exempt on purpose: an implementer's turn ending IS its report to the supervisor.
    /// </summary>
    [Theory]
    [InlineData("implementer")]
    [InlineData("reviewer")]
    [InlineData("")]
    public void MembersAreNotHeldToIt(string role)
    {
        Write_Plan("- [ ] still to do\n");

        Assert.False(Blocks(role));
    }

    /// <summary>
    /// ONE DEMAND AT A TIME. The ledger hook asks for a PLAN.md write; both blocking at once gives a
    /// session two instructions and no order to do them in. Enforcement delayed, never skipped.
    /// </summary>
    [Theory]
    [InlineData(".ledger-behind")]
    [InlineData(".awaiting-answer")]
    public void ItDefersToTheOtherEnforcementFlags(string flagFile)
    {
        Write_Plan("- [ ] still to do\n");
        File.WriteAllText(Path.Combine(_orch, flagFile), "");

        Assert.False(Blocks("solo"));
    }

    /// <summary>An enforcement bug must never wedge a session: no ledger, no opinion.</summary>
    [Fact]
    public void NoLedgerMeansNoBlock()
    {
        File.Delete(Path.Combine(_orch, "PLAN.md"));

        Assert.False(Blocks("solo"));
    }

    void Write_Plan(string body) => File.WriteAllText(Path.Combine(_orch, "PLAN.md"), "# PLAN\n" + body);

    void Write_Channel(string body) => File.WriteAllText(Path.Combine(_orch, "owner-channel.md"), body);

    bool Blocks(string role)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Find_Bash_OrFail(),
            Arguments = $"\"{Find_Hook_OrFail().Replace('\\', '/')}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.Environment["HOME"] = _home;
        startInfo.Environment["AIORCH_ROLE"] = role;
        startInfo.Environment["AIORCH_ID"] = "test-1";

        using var process = Process.Start(startInfo) ?? throw new Exception("could not start bash");

        var output = process.StandardOutput.ReadToEnd();

        if (!process.WaitForExit(60_000))
            throw new Exception("the hook did not exit within 60s");

        return output.Contains("\"decision\":\"block\"", StringComparison.Ordinal);
    }

    static string Find_Hook_OrFail()
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var candidate = Path.Combine(folder, "kit", "hooks", "run-to-the-end-check.sh");

            if (File.Exists(candidate))
                return candidate;

            var parent = Directory.GetParent(folder);

            if (parent == null)
                break;

            folder = parent.FullName;
        }

        throw new Exception($"kit/hooks/run-to-the-end-check.sh not found walking up from {AppContext.BaseDirectory}");
    }

    static string Find_Bash_OrFail()
    {
        string[] candidates =
        [
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files\Git\usr\bin\bash.exe",
            @"C:\Windows\System32\bash.exe",
        ];

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new Exception(
            $"No bash found at any of: {string.Join(", ", candidates)}. This hook is bash and cannot be checked without it; "
            + "passing without running it is the failure mode the test exists to avoid.");
    }
}
