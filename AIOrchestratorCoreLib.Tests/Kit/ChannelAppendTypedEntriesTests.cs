using System.Diagnostics;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// THE TOOL, RUN FOR REAL — E3's "a malformed call is refused BEFORE the write with the missing
/// fields named", and the two requirements only the tool can satisfy.
///
/// <para>
/// WHY THE REAL SCRIPT AND NOT A MODEL OF IT. The whole value of E3 is that the entry a session
/// writes is validated by something that runs, and a test that reimplemented the validation in C#
/// would pass while the shipped bash did anything at all. These invoke <c>kit/bin/channel-append.sh</c>
/// with a real channel file in a temp folder and read what it wrote — or, for a refusal, prove the
/// file is byte-for-byte unchanged, which is the half that matters: "refused" and "refused after
/// writing" are the same exit code and very different facts.
/// </para>
/// <para>
/// THEY SKIP WHERE THEY CANNOT RUN rather than passing: no bash, or no jq, means the harness could
/// not exercise what it claims to (decision 20). A skip is visible in the suite's nine; a green that
/// ran nothing is not.
/// </para>
/// </summary>
public class ChannelAppendTypedEntriesTests : IDisposable
{
    readonly string _temp;
    readonly string _channel;
    readonly string? _script;
    readonly string? _grammar;

    public ChannelAppendTypedEntriesTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), $"aiorch-e3-tool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_temp);

        _channel = Path.Combine(_temp, "owner-channel.md");
        File.WriteAllText(_channel, "# OWNER CHANNEL\n\n---\n");

        _script = ChannelAppendTool.ScriptPath;
        _grammar = ChannelAppendTool.GrammarPath;
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_temp);
    }

    /// <summary>
    /// THE HAPPY PATH, and every claim E3 makes about it at once: the entry is written, its index and
    /// stamp were computed by the TOOL (the caller passed neither), the declared type is PERSISTED,
    /// and the body is composed from the grammar's own marker words.
    ///
    /// The stamp is asserted by SHAPE, not by value — a test that pinned the minute would be a test
    /// that fails at midnight.
    /// </summary>
    [RequiresChannelToolFact]
    [Trait("Speed", "Slow")]
    public void AWellFormedQuestionIsWritten_WithTheToolsOwnIndexAndStamp()
    {
        var run = Run(
            "--to", "owner", "--subject", "the merge gate",
            "--question", "merge now, or hold?", "--option", "Merge it", "--option", "Hold",
            "--recommend", "Merge — nothing else touches it", "--risk", "low", "--row", "FIN-D-277");

        Assert.Equal(0, run.ExitCode);

        // The tool prints the index it allocated, and this is the first entry in a fresh channel.
        Assert.Equal("1", run.StdOut.Trim());

        var written = File.ReadAllText(_channel);

        Assert.Contains("## [1] FROM supervisor — ", written);
        Assert.Matches(@"## \[1\] FROM supervisor — \d{4}-\d{2}-\d{2} \d{2}:\d{2} — the merge gate", written);

        // The persisted type, on its own line under the header (requirement 3).
        Assert.Contains($"{ChannelGrammar.TypeLine_Prefix}question", written);

        // Composed from the grammar, not from a literal in the tool.
        Assert.Contains($"{ChannelGrammar.QUESTION} merge now, or hold?", written);
        Assert.Contains($"{ChannelGrammar.OPTION} Merge it", written);
        Assert.Contains($"{ChannelGrammar.RECOMMEND} Merge — nothing else touches it", written);
        Assert.Contains($"{ChannelGrammar.RISK} low", written);
        Assert.Contains($"{ChannelGrammar.ROW} FIN-D-277", written);
    }

    /// <summary>
    /// THE APP READS BACK WHAT THE TOOL WROTE — the one claim neither side can make alone, and the
    /// point of a single grammar. It also proves the type line is METADATA: the parser lifts it into
    /// <c>Type</c> and takes it OUT of the body, because a mirrored "type: question" is the app's
    /// bookkeeping read aloud on the owner's phone.
    /// </summary>
    [RequiresChannelToolFact]
    [Trait("Speed", "Slow")]
    public void WhatTheToolWrites_TheAppParsesAsATypedEntry()
    {
        Assert.Equal(0, Run(
            "--to", "owner", "--subject", "the merge gate",
            "--question", "merge now, or hold?", "--option", "Merge it", "--option", "Hold").ExitCode);

        var entry = Assert.Single(ChannelEntry_Parser.Parse_All(File.ReadAllText(_channel)));

        Assert.Equal(1, entry.Index);
        Assert.Equal("question", entry.Type);
        Assert.Equal("the merge gate", entry.Subject);
        Assert.DoesNotContain(ChannelGrammar.TypeLine_Prefix, entry.Body);
        Assert.Contains(ChannelGrammar.QUESTION, entry.Body);

        // The audit trail keeps it: RawText is the entry as written.
        Assert.Contains(ChannelGrammar.TypeLine_Prefix, entry.RawText);
    }

    /// <summary>
    /// REFUSED BEFORE THE WRITE, WITH THE FIELD NAMED — the Done-when clause, and the file is proved
    /// UNCHANGED. Reporting a refusal after appending would be the same exit code and the opposite
    /// outcome.
    /// </summary>
    [RequiresChannelToolTheory]
    [Trait("Speed", "Slow")]
    [InlineData(new[] { "--question", "which?", "--option", "only one" }, "at least 2")]
    [InlineData(new[] { "--question", "which?", "--option", "a", "--option", "b", "--option", "c", "--option", "d", "--option", "e" }, "at most 4")]
    [InlineData(new[] { "--option", "orphan", "--option", "second" }, "without --question")]
    [InlineData(new[] { "--question", "which?", "--option", "a", "--option", "this label is far too long to fit on a telephone button" }, "characters")]
    [InlineData(new[] { "--question", "which?", "--option", "a", "--option", "b", "--risk", "catastrophic" }, "--risk must be one of")]
    [InlineData(new[] { "--type", "nonsense", "--report", "hello" }, "not a type this app knows")]
    public void AMalformedCallIsRefusedAndNothingIsWritten(string[] extraArguments, string expectedInTheComplaint)
    {
        var before = File.ReadAllBytes(_channel);

        var run = Run([.. new[] { "--to", "owner", "--subject", "x" }, .. extraArguments]);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains(expectedInTheComplaint, run.StdErr);
        Assert.Contains("NOTHING WAS WRITTEN", run.StdErr);
        Assert.Equal(before, File.ReadAllBytes(_channel));
    }

    /// <summary>
    /// EVERY FAULT AT ONCE, not the first one. A caller told "missing --option" fixes that and is
    /// then told "too long", which is two round trips for one entry — and a session's round trip is
    /// a model turn.
    /// </summary>
    [RequiresChannelToolFact]
    [Trait("Speed", "Slow")]
    public void AllTheFaultsAreNamedTogether()
    {
        var run = Run(
            "--to", "owner", "--subject", "x",
            "--question", "which?", "--option", "only one", "--risk", "catastrophic");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("at least 2", run.StdErr);
        Assert.Contains("--risk must be one of", run.StdErr);
    }

    /// <summary>
    /// REQUIREMENT 2 — a member cannot sign as the supervisor. `--author` accepted any word, and an
    /// entry is believed because of the name on it: that is how a model choice got erased from a
    /// brief. The refusal names BOTH identities, so the log says what was claimed and what was true.
    /// </summary>
    [RequiresChannelToolFact]
    [Trait("Speed", "Slow")]
    public void AMemberCannotSignAsTheSupervisor()
    {
        var before = File.ReadAllBytes(_channel);

        var run = Run(
            new Dictionary<string, string> { ["AIORCH_ROLE"] = "implementer", ["AIORCH_MEMBER"] = "imp-2" },
            "--author", "supervisor", "--subject", "x", "--report", "hello");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("REFUSED", run.StdErr);
        Assert.Contains("supervisor", run.StdErr);
        Assert.Contains("imp-2", run.StdErr);
        Assert.Equal(before, File.ReadAllBytes(_channel));
    }

    /// <summary>
    /// AND THE AUTHOR IS THE SESSION'S EVEN WHEN NOBODY PASSES ONE — the flag is at best a
    /// restatement of what the process already knows, so omitting it is the normal case.
    /// </summary>
    [RequiresChannelToolFact]
    [Trait("Speed", "Slow")]
    public void TheAuthorIsTakenFromTheSessionWhenNoFlagIsGiven()
    {
        Assert.Equal(0, Run(
            new Dictionary<string, string> { ["AIORCH_ROLE"] = "implementer", ["AIORCH_MEMBER"] = "imp-2" },
            "--subject", "progress", "--report", "the parser is done").ExitCode);

        Assert.Contains("FROM imp-2 — ", File.ReadAllText(_channel));
    }

    /// <summary>
    /// AN OWNER QUESTION ON A MEMBER SPOKE IS A QUESTION NOBODY ANSWERS, so `--to owner` is checked
    /// against the channel it was handed. This is a real confusion and not a formality: the two paths
    /// differ only in a file name.
    /// </summary>
    [RequiresChannelToolFact]
    [Trait("Speed", "Slow")]
    public void AnOwnerQuestionAimedAtAMemberSpokeIsRefused()
    {
        var spoke = Path.Combine(_temp, "channel.md");
        File.WriteAllText(spoke, "# IMP-2\n\n---\n");

        var run = Run_Against(
            new Dictionary<string, string> { ["AIORCH_ROLE"] = "supervisor" }, spoke,
            "--to", "owner", "--subject", "x", "--question", "which?", "--option", "a", "--option", "b");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("not an owner channel", run.StdErr);
    }

    /// <summary>
    /// THE UNTYPED PATH STILL WORKS — the transition the brief requires: "a session on the old skill
    /// is never mute". A plain `--body-file` call writes exactly what it always did, with no type
    /// line, and the app parses it with <c>Type</c> null.
    /// </summary>
    [RequiresChannelToolFact]
    [Trait("Speed", "Slow")]
    public void TheOldUntypedCallStillWrites_AndParsesAsUntyped()
    {
        var body = Path.Combine(_temp, "body.md");
        File.WriteAllText(body, "the old way, still working\n");

        Assert.Equal(0, Run("--author", "supervisor", "--subject", "a plain entry", "--body-file", body).ExitCode);

        var entry = Assert.Single(ChannelEntry_Parser.Parse_All(File.ReadAllText(_channel)));

        Assert.Null(entry.Type);
        Assert.Contains("the old way, still working", entry.Body);
        Assert.DoesNotContain(ChannelGrammar.TypeLine_Prefix, entry.RawText);
    }

    // ── running the real thing ──────────────────────────────────────────────────────────────────

    (int ExitCode, string StdOut, string StdErr) Run(params string[] arguments)
    {
        return Run(new Dictionary<string, string> { ["AIORCH_ROLE"] = "supervisor" }, arguments);
    }

    (int ExitCode, string StdOut, string StdErr) Run(IDictionary<string, string> environment, params string[] arguments)
    {
        return Run_Against(environment, _channel, arguments);
    }

    /// <summary>
    /// DISTINCTLY NAMED, not a third `Run` overload — three of these tests failed because C# bound
    /// `Run(dictionary, "--author", "supervisor", …)` to the channel-taking overload and passed
    /// "--author" as the channel. A `params` tail next to a positional string of the same type is
    /// the trap `TopicStatusFields` records for its own parameters, in a test harness this time.
    /// </summary>
    (int ExitCode, string StdOut, string StdErr) Run_Against(IDictionary<string, string> environment, string channel, params string[] arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = Bash_Locator.Find_OrFail(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _temp,
        };

        start.ArgumentList.Add(_script!);
        start.ArgumentList.Add("--channel");
        start.ArgumentList.Add(channel);

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        // The grammar is pointed at explicitly: the tool finds it beside itself in a shipped kit, and
        // the repo layout is the same shape, but naming it here means this test cannot pass because
        // of a grammar it did not intend to read.
        start.Environment["AIORCH_CHANNEL_GRAMMAR"] = _grammar!;

        foreach (var (key, value) in environment)
            start.Environment[key] = value;

        using var process = Process.Start(start)
            ?? throw new Exception("could not start bash");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        process.WaitForExit(30_000);

        return (process.ExitCode, stdout, stderr);
    }

}
