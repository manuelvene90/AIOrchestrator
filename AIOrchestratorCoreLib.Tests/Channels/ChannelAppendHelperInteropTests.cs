using System.Diagnostics;
using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// The protocol is only worth anything if BOTH sides implement it identically, so these run the
/// REAL kit/channel-append.sh against the REAL <see cref="ChannelFile_Lock"/>. A .NET-only test of
/// the .NET half would prove the app cannot collide with itself, which was never the open question.
/// <para>
/// Nothing here skips. If bash or the script cannot be found, the test FAILS: a harness that cannot
/// locate what it tests must refuse to run rather than return green, because nothing-found reads as
/// nothing-wrong. That mistake produced 16 confident findings about code that was never executed.
/// </para>
/// </summary>
[Collection(CHANNEL_LOCK_COLLECTION.NAME)]
public class ChannelAppendHelperInteropTests : IDisposable
{
    readonly string _tempFolder;
    readonly string _channelFile;

    public ChannelAppendHelperInteropTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-helper-interop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _channelFile = Path.Combine(_tempFolder, "channel.md");
        File.WriteAllText(_channelFile, "seed\n\n---\n");
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    [Fact]
    public void Helper_AppendsAWellFormedEntryTheParserCanRead()
    {
        var bodyFile = Path.Combine(_tempFolder, "body.txt");
        File.WriteAllText(bodyFile, "the body of the entry\n");

        var run = Run_Helper($"--channel \"{To_BashPath(_channelFile)}\" --author implementer --subject \"a subject\" --body-file \"{To_BashPath(bodyFile)}\"");

        Assert.True(run.ExitCode == 0, $"helper failed: {run.StandardError}");

        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile));

        Assert.Single(entries);
        Assert.Equal(1, entries[0].Index);
        Assert.Equal(ChannelAuthors.Implementer, entries[0].Author);
        Assert.Equal("a subject", entries[0].Subject);
        Assert.Equal("the body of the entry", entries[0].Body);
    }

    /// <summary>
    /// The whole point of the protocol: a lock taken by the .NET side must stop the bash side, and
    /// the caller must be TOLD, with an exit code it can distinguish from success.
    /// </summary>
    [Fact]
    public void Helper_WhenTheAppHoldsTheLock_RefusesToWriteAndSaysSo()
    {
        var bodyFile = Path.Combine(_tempFolder, "body.txt");
        File.WriteAllText(bodyFile, "must not be written\n");

        HelperRun run = default;

        ChannelFile_Lock.Try_Run_WithLock(_channelFile, TimeSpan.FromSeconds(2), () =>
        {
            run = Run_Helper(
                $"--channel \"{To_BashPath(_channelFile)}\" --author implementer --subject \"blocked\" "
                + $"--body-file \"{To_BashPath(bodyFile)}\" --budget-seconds 1");
        }, out _);

        Assert.Equal(3, run.ExitCode);
        Assert.Contains("COULD NOT ACQUIRE", run.StandardError);
        Assert.DoesNotContain("must not be written", File.ReadAllText(_channelFile));

        // A negative assertion admits every other string in the language, so pin what the file must
        // actually still BE: untouched, with no entry at all.
        Assert.Empty(ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile)));
    }

    /// <summary>The mirror image: a lock the bash side holds must stop the app.</summary>
    [Fact]
    public void App_WhenAHelperHoldsTheLock_DoesNotWrite()
    {
        var lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(_channelFile);
        Directory.CreateDirectory(lockDirectory);

        File.WriteAllText(
            Path.Combine(lockDirectory, ChannelFile_Lock.OWNER_FILE_NAME),
            $"pid=4242\nutc={DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\nrole=session\n");

        var ran = false;

        var acquired = ChannelFile_Lock.Try_Run_WithLock(_channelFile, TimeSpan.FromMilliseconds(500), () => ran = true, out _);

        Assert.False(acquired);
        Assert.False(ran);
    }

    /// <summary>
    /// Both sides must agree on what a STALE lock looks like, or one of them wedges forever while
    /// the other walks straight past. This writes the owner file the way the SCRIPT writes it and
    /// checks the app reads it — the format is the contract.
    /// </summary>
    [Fact]
    public void App_ReadsTheStalenessStampTheScriptWrites()
    {
        var lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(_channelFile);
        Directory.CreateDirectory(lockDirectory);

        // Exactly the shape of the script's printf: pid, utc in -u +%Y-%m-%dT%H:%M:%SZ, role.
        var staleStamp = DateTime.UtcNow.AddSeconds(-(ChannelFile_Lock.STALE_SECONDS + 30));

        File.WriteAllText(
            Path.Combine(lockDirectory, ChannelFile_Lock.OWNER_FILE_NAME),
            $"pid=4242\nutc={staleStamp:yyyy-MM-ddTHH:mm:ssZ}\nrole=session\n");

        var ran = false;
        var acquired = ChannelFile_Lock.Try_Run_WithLock(_channelFile, TimeSpan.FromSeconds(5), () => ran = true, out _);

        Assert.True(acquired, "the app did not recognise the script's own stamp format as stale");
        Assert.True(ran);
    }

    /// <summary>
    /// Concurrency across the boundary, which is the case that actually happens in production: the
    /// app appending while sessions append.
    /// </summary>
    [Fact]
    public void HelperAndApp_AppendingTogether_ProduceDistinctIndicesAndNoTornEntries()
    {
        var bodyFile = Path.Combine(_tempFolder, "big.txt");
        File.WriteAllText(bodyFile, string.Concat(Enumerable.Range(1, 400).Select(i => $"HELPER-BODY-{i}\n")));

        var helpers = Enumerable.Range(1, 3)
            .Select(i => Task.Run(() => Run_Helper(
                $"--channel \"{To_BashPath(_channelFile)}\" --author implementer --subject \"helper {i}\" "
                + $"--body-file \"{To_BashPath(bodyFile)}\" --budget-seconds 30")))
            .ToArray();

        // Append_AppEntry takes the lock itself — wrapping it in another acquire would self-block,
        // because the lock is deliberately NOT reentrant (see ChannelFile_Lock).
        var appWrites = Enumerable.Range(1, 3)
            .Select(i => Task.Run(() =>
                ChannelAppender.Append_AppEntry(_channelFile, $"app {i}", "app body", DateTime.Now)))
            .ToArray();

        Task.WaitAll([.. helpers, .. appWrites]);

        foreach (var helper in helpers)
            Assert.True(helper.Result.ExitCode == 0, $"helper failed: {helper.Result.StandardError}");

        foreach (var appWrite in appWrites)
            Assert.True(appWrite.Result, "the app could not acquire the lock within 30s");

        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile));

        Assert.Equal(6, entries.Count);
        Assert.Equal(6, entries.Select(e => e.Index).Distinct().Count());

        // Torn-entry check: every helper entry must still carry its whole body. An entry whose body
        // was split by another writer's header would have lost the lines after the split point.
        foreach (var helperEntry in entries.Where(e => e.Subject.StartsWith("helper ")))
            Assert.Contains("HELPER-BODY-400", helperEntry.Body);
    }

    /// <summary>
    /// The other direction, and the one that would otherwise be assumed: the SCRIPT reading a lock
    /// the APP wrote. The owner file here is produced by the app's own
    /// <see cref="ChannelFile_Lock.Build_OwnerFileContent"/>, not by a format restated in the test —
    /// otherwise this would only prove the author can copy a string twice.
    /// </summary>
    [Fact]
    public void Helper_BreaksAStaleLockWrittenByTheApp()
    {
        var lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(_channelFile);
        Directory.CreateDirectory(lockDirectory);

        File.WriteAllText(
            Path.Combine(lockDirectory, ChannelFile_Lock.OWNER_FILE_NAME),
            ChannelFile_Lock.Build_OwnerFileContent(
                processId: 4242,
                heldSinceUtc: DateTime.UtcNow.AddSeconds(-(ChannelFile_Lock.STALE_SECONDS + 30)),
                role: "app"));

        var bodyFile = Path.Combine(_tempFolder, "body.txt");
        File.WriteAllText(bodyFile, "written after breaking a dead app lock\n");

        var run = Run_Helper(
            $"--channel \"{To_BashPath(_channelFile)}\" --author implementer --subject \"after break\" "
            + $"--body-file \"{To_BashPath(bodyFile)}\" --budget-seconds 10");

        Assert.True(run.ExitCode == 0, $"the helper did not break the app's stale lock: {run.StandardError}");

        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile));

        Assert.Single(entries);
        Assert.Equal("after break", entries[0].Subject);
    }

    /// <summary>
    /// A FRESH lock written by the app must be respected by the script — the same parse, the
    /// opposite verdict. Without this, a script that treated every app lock as stale would pass the
    /// break test above and still be catastrophically wrong.
    /// </summary>
    [Fact]
    public void Helper_RespectsAFreshLockWrittenByTheApp()
    {
        var lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(_channelFile);
        Directory.CreateDirectory(lockDirectory);

        File.WriteAllText(
            Path.Combine(lockDirectory, ChannelFile_Lock.OWNER_FILE_NAME),
            ChannelFile_Lock.Build_OwnerFileContent(4242, DateTime.UtcNow, "app"));

        var bodyFile = Path.Combine(_tempFolder, "body.txt");
        File.WriteAllText(bodyFile, "must not be written\n");

        var run = Run_Helper(
            $"--channel \"{To_BashPath(_channelFile)}\" --author implementer --subject \"blocked\" "
            + $"--body-file \"{To_BashPath(bodyFile)}\" --budget-seconds 1");

        Assert.Equal(3, run.ExitCode);
        Assert.Empty(ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile)));
    }

    /// <summary>
    /// The helper's half of the real invariant: an append must BEGIN A LINE. Both sides have to
    /// agree here too — a channel is written by both, so one writer that ran an entry onto the
    /// previous line would corrupt a file the other had kept well-formed.
    /// </summary>
    [Fact]
    public void Helper_AppendingToAFileNotEndingInANewline_StillStartsTheHeaderOnItsOwnLine()
    {
        File.WriteAllText(_channelFile, "seed\n\n---");

        var bodyFile = Path.Combine(_tempFolder, "body.txt");
        File.WriteAllText(bodyFile, "a body\n");

        var run = Run_Helper($"--channel \"{To_BashPath(_channelFile)}\" --author implementer --subject \"pressed\" --body-file \"{To_BashPath(bodyFile)}\"");

        Assert.True(run.ExitCode == 0, $"helper failed: {run.StandardError}");

        var text = File.ReadAllText(_channelFile);

        Assert.Single(ChannelEntry_Parser.Parse_All(text));
        Assert.Contains("\n---\n", text);
        Assert.DoesNotContain("---##", text);
        Assert.EndsWith("\n", text);
    }

    /// <summary>
    /// The bash half of the abandoned-lock recovery. Both sides must agree, or one wedges on a
    /// state the other walks past.
    /// </summary>
    [Fact]
    public void Helper_BreaksAnAbandonedMetadataLessLock()
    {
        var lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(_channelFile);
        Directory.CreateDirectory(lockDirectory);
        Directory.SetLastWriteTimeUtc(lockDirectory, DateTime.UtcNow.AddSeconds(-(ChannelFile_Lock.STALE_SECONDS + 30)));

        var bodyFile = Path.Combine(_tempFolder, "body.txt");
        File.WriteAllText(bodyFile, "written after clearing an abandoned lock\n");

        var run = Run_Helper(
            $"--channel \"{To_BashPath(_channelFile)}\" --author implementer --subject \"after abandon\" "
            + $"--body-file \"{To_BashPath(bodyFile)}\" --budget-seconds 10");

        Assert.True(run.ExitCode == 0, $"the helper could not clear an abandoned metadata-less lock: {run.StandardError}");
        Assert.Single(ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile)));
    }

    /// <summary>
    /// The disjoint half: a metadata-less lock that is FRESH is a writer part-way through
    /// acquiring, and must still be respected. Without this, "break any lock with no owner file"
    /// would pass the test above and destroy live locks.
    /// </summary>
    [Fact]
    public void Helper_RespectsAFreshMetadataLessLock()
    {
        Directory.CreateDirectory(ChannelFile_Lock.Build_LockDirectoryPath(_channelFile));

        var bodyFile = Path.Combine(_tempFolder, "body.txt");
        File.WriteAllText(bodyFile, "must not be written\n");

        var run = Run_Helper(
            $"--channel \"{To_BashPath(_channelFile)}\" --author implementer --subject \"blocked\" "
            + $"--body-file \"{To_BashPath(bodyFile)}\" --budget-seconds 1");

        Assert.Equal(3, run.ExitCode);
        Assert.Empty(ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile)));
    }

    /// <summary>
    /// The two sides must count the SAME headers. The C# parser accepts any whitespace after the
    /// hashes; the helper's scanner required exactly one space, so a header written "##  [82]"
    /// was invisible to bash and visible to the app — and bash would then mint an index that
    /// already existed. Index allocation inside the lock is what made the duplicate-index defect
    /// one deliverable instead of two; two scanners that disagree hands it straight back.
    /// </summary>
    [Fact]
    public void Helper_CountsTheSameHeadersTheParserDoes_NotOnlyTheCanonicallySpacedOnes()
    {
        File.WriteAllText(_channelFile, "seed\n\n##  [82] FROM supervisor — 2026-08-13 21:00 — oddly spaced but real\n\nbody\n");

        // The parser sees it, so the next index is 83.
        Assert.Equal(83, ChannelEntry_Parser.Get_NextIndex(File.ReadAllText(_channelFile)));

        var bodyFile = Path.Combine(_tempFolder, "body.txt");
        File.WriteAllText(bodyFile, "the next entry\n");

        var run = Run_Helper($"--channel \"{To_BashPath(_channelFile)}\" --author implementer --subject \"next\" --body-file \"{To_BashPath(bodyFile)}\"");

        Assert.True(run.ExitCode == 0, $"helper failed: {run.StandardError}");
        Assert.Equal("83", run.StandardOutput.Trim());
    }

    /// <summary>
    /// The bash half of the future-stamp guard. Both sides must agree here or one of them wedges on
    /// a lock the other walks past — and the stamp that causes it is written by whichever language
    /// happened to acquire, so the skew is not hypothetical.
    /// </summary>
    [Fact]
    public void Helper_BreaksALockStampedInTheFuture()
    {
        var lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(_channelFile);
        Directory.CreateDirectory(lockDirectory);

        File.WriteAllText(
            Path.Combine(lockDirectory, ChannelFile_Lock.OWNER_FILE_NAME),
            ChannelFile_Lock.Build_OwnerFileContent(4242, DateTime.UtcNow.AddHours(10), "app", "dead-holder"));

        Directory.SetLastWriteTimeUtc(lockDirectory, DateTime.UtcNow.AddSeconds(-(ChannelFile_Lock.STALE_SECONDS + 30)));

        var bodyFile = Path.Combine(_tempFolder, "body.txt");
        File.WriteAllText(bodyFile, "written after breaking a future-stamped lock\n");

        var run = Run_Helper(
            $"--channel \"{To_BashPath(_channelFile)}\" --author implementer --subject \"after future stamp\" "
            + $"--body-file \"{To_BashPath(bodyFile)}\" --budget-seconds 10");

        Assert.True(run.ExitCode == 0, $"the helper could not break a future-stamped lock: {run.StandardError}");
        Assert.Single(ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile)));
    }

    /// <summary>The disjoint half, on the bash side: a future stamp on a FRESH lock is respected.</summary>
    [Fact]
    public void Helper_RespectsAFreshLockStampedInTheFuture()
    {
        var lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(_channelFile);
        Directory.CreateDirectory(lockDirectory);

        File.WriteAllText(
            Path.Combine(lockDirectory, ChannelFile_Lock.OWNER_FILE_NAME),
            ChannelFile_Lock.Build_OwnerFileContent(4242, DateTime.UtcNow.AddHours(10), "app", "live-holder"));

        var bodyFile = Path.Combine(_tempFolder, "body.txt");
        File.WriteAllText(bodyFile, "must not be written\n");

        var run = Run_Helper(
            $"--channel \"{To_BashPath(_channelFile)}\" --author implementer --subject \"blocked\" "
            + $"--body-file \"{To_BashPath(bodyFile)}\" --budget-seconds 1");

        Assert.Equal(3, run.ExitCode);
        Assert.Empty(ChannelEntry_Parser.Parse_All(File.ReadAllText(_channelFile)));
    }

    readonly record struct HelperRun(int ExitCode, string StandardOutput, string StandardError);

    static HelperRun Run_Helper(string arguments)
    {
        var bashPath = Find_Bash_OrFail();
        var scriptPath = Find_Script_OrFail();

        var startInfo = new ProcessStartInfo
        {
            FileName = bashPath,
            Arguments = $"\"{To_BashPath(scriptPath)}\" {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new Exception($"could not start bash at '{bashPath}'");

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(60_000))
            throw new Exception($"channel-append.sh did not exit within 60s (args: {arguments})");

        return new HelperRun(process.ExitCode, standardOutput, standardError);
    }

    static string To_BashPath(string windowsPath)
    {
        return windowsPath.Replace('\\', '/');
    }

    /// <summary>
    /// Walks up from the test binary to the repo root. Fails loudly rather than returning null: a
    /// missing script must stop the test, never quietly turn it into a no-op that passes.
    /// </summary>
    static string Find_Script_OrFail()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "kit", "channel-append.sh");

            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new Exception(
            $"kit/channel-append.sh was not found walking up from '{AppContext.BaseDirectory}'. "
            + "This test asserts on the real script; without it there is nothing under test and a pass would be meaningless.");
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
            $"No bash found at any of: {string.Join(", ", candidates)}. The append protocol has a bash half and a .NET half; "
            + "without bash this suite cannot show they agree, and passing without checking is the failure mode this test exists to avoid.");
    }
}
