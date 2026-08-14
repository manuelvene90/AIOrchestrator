using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// THE PRODUCER OF THE TYPED EXCEPTION, PINNED — the half `TopicNameSyncGateTests` is blind to.
///
/// `Classify_Failure` is pinned six ways: it is a pure function and the suite hammers it. But it can
/// only classify what it is HANDED, and nothing asserted that the client hands it a
/// <see cref="AIOrchestratorCoreLib.Telegram.TelegramApiClient.TelegramApiException"/> at all. rev-11
/// measured the gap: revert all four status-bearing throw sites to a plain `Exception` — the exact
/// pre-fix state — and the suite stays at 709 passed, 0 failed. Not one test moves.
///
/// The failure that makes it worth pinning NOW is not generic: a merge resolution on one of the 23 live
/// branches turns one throw back into a plain `Exception`, topic sync silently returns to "one 429 pins
/// a stale glyph until restart", and the suite is green. We are about to do that merge.
///
/// WHY THIS ASSERTS SOURCE TEXT, WHICH IS NORMALLY THE WRONG INSTRUMENT. A behavioural test would need
/// the client to be reachable and its transport substitutable: `TelegramApiClientModel` is
/// `internal sealed` with no `InternalsVisibleTo`, it constructs its own `HttpClient` in its
/// constructor, and it builds `https://api.telegram.org/...` inline. There is no seam, and adding one
/// is a production change this test was explicitly scoped not to make.
///
/// The saving grace is that the property being protected IS a source-level property — WHICH EXCEPTION
/// TYPE IS CONSTRUCTED — and the hazard is a source-level edit during a merge. So this is not a proxy
/// for the real thing; it is the real thing, read where it is written. It is NOT a substitute for the
/// behavioural test: if the client ever gains a transport seam, that test should be written and this one
/// can go.
///
/// IT REFUSES TO RUN RATHER THAN PASS VACUOUSLY. A source-reading harness that cannot find its target
/// finds no offences either, and no-offences-is-a-pass is exactly how a guard certifies code it never
/// read (CLAUDE.md item 20). Locating the file is therefore asserted before anything is measured.
/// </summary>
public class TelegramApiClientThrowSiteTests
{
    const string CLIENT_RELATIVE_PATH = "AIOrchestratorCoreLib/Telegram/TelegramApiClient/TelegramApiClientModel.cs";

    /// <summary>
    /// EVERY THROW THAT KNOWS A STATUS MUST CARRY IT. The four sites are `sendPhoto`, `getUpdates`, the
    /// file download and the shared `Post_Async`; each had `response.StatusCode` in scope and formatted
    /// it into a message string, which is how the code came to be discarded at the moment it was known.
    ///
    /// Asserted as a PROPERTY rather than a count of four, so adding a fifth status-bearing endpoint
    /// cannot quietly reintroduce the defect while the number still matches — a count is satisfied by
    /// the wrong four.
    /// </summary>
    [Fact]
    public void NoThrowSiteThatKnowsTheStatusCodeDiscardsIt()
    {
        var source = Read_ClientSource();

        var offenders = source
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains("throw new Exception(") && line.Contains("StatusCode"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} throw site(s) still discard the status code into a message string:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// AND THE POSITIVE HALF, asserted apart. Without it the case above passes on a file that throws
    /// nothing at all — a deletion would read as a fix, which is the two-routes-to-one-green trap.
    /// </summary>
    [Fact]
    public void TheStatusBearingThrowSitesUseTheTypedException()
    {
        var source = Read_ClientSource();

        Assert.Contains("throw new TelegramApiException(", source);
    }

    /// <summary>
    /// THE GUARD ON THE GUARD. Returns the source or FAILS — never an empty string, because a harness
    /// that cannot find what it tests must refuse to run rather than certify the absence of the thing
    /// it never read.
    /// </summary>
    static string Read_ClientSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, CLIENT_RELATIVE_PATH.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new Exception(
            $"Could not locate '{CLIENT_RELATIVE_PATH}' walking up from '{AppContext.BaseDirectory}'. "
            + "This harness reads the client's SOURCE, so a missing file means it measured nothing — "
            + "failing rather than reporting zero offences.");
    }
}
