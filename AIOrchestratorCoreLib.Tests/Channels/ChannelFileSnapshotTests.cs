using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// The field that tells the NEXT malformed-header occurrence whether any writer touched the file
/// while the app was reading it.
///
/// <para>
/// Three hypotheses died on the two 2026-08-13 occurrences — a missing blank line, a drifted second
/// regex, and a read landing mid-append — and each died of argument at 21:00 rather than of
/// evidence, because the only evidence was the offending line's TEXT. `imp-2` then named a writer
/// nobody had ruled out: the compactor rewrites the live file WHOLESALE. That one is ruled out for
/// those two events (that channel has never been compacted, it has no `.archive.md` sibling) and is
/// not ruled out for the next one.
/// </para>
/// <para>
/// So the verdict these cases pin is a COMPARISON, never a stamp. A single length at report time
/// would render fine in the log and answer nothing.
/// </para>
/// </summary>
public class ChannelFileSnapshotTests : IDisposable
{
    readonly string _tempRoot;

    public ChannelFileSnapshotTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-file-snapshot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Take_ReportsTheFilesRealLengthAndStamp()
    {
        var file = Path.Combine(_tempRoot, "channel.md");
        File.WriteAllText(file, "## [1] FROM supervisor — d — s\n");

        var snapshot = ChannelFile_Snapshot.Take_OrUnknown(file);

        Assert.True(snapshot.WasTaken);
        Assert.Equal(new FileInfo(file).Length, snapshot.LengthBytes);
        Assert.Equal(File.GetLastWriteTimeUtc(file), snapshot.LastWriteUtc);
    }

    /// <summary>
    /// A file that cannot be stat-ed is UNKNOWN and says which half failed — never a zero-length
    /// file, which is a confident wrong answer of exactly the kind this whole field exists to stop
    /// (decision 21: a check that cannot evaluate its predicate says so and names it).
    /// </summary>
    [Fact]
    public void AMissingFile_IsUNKNOWN_AndTheFieldNamesWhichHalfCouldNotBeStatted()
    {
        var snapshot = ChannelFile_Snapshot.Take_OrUnknown(Path.Combine(_tempRoot, "not-here.md"));

        Assert.False(snapshot.WasTaken);
        Assert.Equal(0, snapshot.LengthBytes);

        var taken = new ChannelFileSnapshot(true, 100, DateTime.UtcNow);

        Assert.Contains("file=UNKNOWN(could-not-stat:before-read)", ChannelFile_Snapshot.Describe_ChangeAcrossRead(snapshot, taken));
        Assert.Contains("file=UNKNOWN(could-not-stat:at-report)", ChannelFile_Snapshot.Describe_ChangeAcrossRead(taken, snapshot));
    }

    [Fact]
    public void AFileThatDidNotMoveAcrossTheRead_IsUNCHANGED_AndStillCarriesItsLengthAndStamp()
    {
        var stamp = new DateTime(2026, 8, 14, 10, 38, 12, DateTimeKind.Utc);
        var snapshot = new ChannelFileSnapshot(true, 189234, stamp);

        var described = ChannelFile_Snapshot.Describe_ChangeAcrossRead(snapshot, snapshot);

        Assert.Contains("file=UNCHANGED-ACROSS-READ", described);
        Assert.Contains("189234B", described);
        Assert.Contains(stamp.ToString("O"), described);
    }

    /// <summary>
    /// THE COMPACTOR'S SIGNATURE: the live file loses thousands of lines between one instant and the
    /// next. Both lengths have to survive into the log — "changed" alone would not tell a wholesale
    /// rewrite from an append, and those implicate different writers.
    /// </summary>
    [Fact]
    public void AFileREWRITTEN_DuringTheRead_IsCHANGED_AndCarriesBOTHLengths()
    {
        var beforeRead = new ChannelFileSnapshot(true, 189234, new DateTime(2026, 8, 14, 10, 38, 12, DateTimeKind.Utc));
        var atReport = new ChannelFileSnapshot(true, 12045, new DateTime(2026, 8, 14, 10, 38, 13, DateTimeKind.Utc));

        var described = ChannelFile_Snapshot.Describe_ChangeAcrossRead(beforeRead, atReport);

        Assert.Contains("file=CHANGED-DURING-READ", described);
        Assert.Contains("189234B", described);
        Assert.Contains("12045B", described);
    }

    /// <summary>
    /// The verdict is not length-only. A wholesale rewrite that happens to land on the same length is
    /// exactly the case a length comparison would call quiet, so the stamp has to count too.
    /// </summary>
    [Fact]
    public void AREWRITE_ToTheSameLength_IsStillCHANGED()
    {
        var beforeRead = new ChannelFileSnapshot(true, 189234, new DateTime(2026, 8, 14, 10, 38, 12, DateTimeKind.Utc));
        var atReport = new ChannelFileSnapshot(true, 189234, new DateTime(2026, 8, 14, 10, 38, 59, DateTimeKind.Utc));

        Assert.Contains("file=CHANGED-DURING-READ", ChannelFile_Snapshot.Describe_ChangeAcrossRead(beforeRead, atReport));
    }

    /// <summary>
    /// ASCII only, on purpose: this field sits beside a hex dump in a report about bytes, and a
    /// separator whose own encoding could be argued about would be one more instrument damaging what
    /// it measures.
    /// </summary>
    [Fact]
    public void TheField_IsASCII_Only()
    {
        var described = ChannelFile_Snapshot.Describe_ChangeAcrossRead(
            new ChannelFileSnapshot(true, 1, DateTime.UtcNow),
            new ChannelFileSnapshot(true, 2, DateTime.UtcNow));

        Assert.All(described, character => Assert.True(character < 128, $"non-ASCII character '{character}' in the diagnostic field: {described}"));
    }
}
