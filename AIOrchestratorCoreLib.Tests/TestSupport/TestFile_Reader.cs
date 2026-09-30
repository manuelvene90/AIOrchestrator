using AIOrchestratorCoreLib.Storage;

namespace AIOrchestratorCoreLib.Tests.TestSupport;

/// <summary>
/// READS A FILE THE RUNNING ENGINE MAY BE WRITING AT THIS INSTANT — the test-side half of the Windows
/// sharing race that <c>e72cc84</c> fixed for production. Use it in place of <c>File.ReadAllText</c>
/// for any file the engine appends to, compacts or atomically replaces (a channel, session.json, a
/// log); a file only the test itself writes does not need it.
///
/// <para>
/// WHY, MEASURED (Windows CI, 2026-09-11 onward): <c>File.ReadAllText</c> opens with
/// <c>FileShare.Read</c>, which DENIES the engine's concurrent writer — <c>File.AppendAllText</c> in
/// <c>ChannelAppender</c> and the rename in <c>Atomic_FileWriter</c> — so whichever side opens second
/// gets <c>IOException: The process cannot access the file '…owner-channel.md' because it is being
/// used by another process</c>. Linux does not honour share modes, which is why it only ever reddened
/// on the Windows leg, and never twice on the same test: it landed on
/// <c>AttachmentsReachThePhoneTests</c> (<c>AMissingFile_IsAWarningAndACoaching_NeverAThrow</c>,
/// <c>AFileOutsideTheAllowedRoots_IsRefusedToTheAgent_AndNothingIsUploaded</c>, through
/// <c>Channel_Text</c>), on <c>QuestionContractProbeTests.Channel</c>, and on both cases of
/// <c>AnnouncementSurvivesALockedChannelTests</c> that poll the channel while the engine runs. The
/// mechanism was racy; the tests it caught were not.
/// </para>
/// <para>
/// PRODUCTION'S READER, NOT A SECOND IMPLEMENTATION (plan 03 Task 11, Step 2). It delegates to
/// <see cref="Tolerant_FileReader"/>, which opens with <c>FileShare.ReadWrite | FileShare.Delete</c>
/// (so it neither blocks the writer nor is blocked by it) and retries a sharing failure a few times
/// over ~200 ms (the atomic rename's delete-pending window). If the engine's read ever needs a
/// different answer, the test's read changes with it — two copies of this loop would drift, and the
/// test copy would then certify a reader production no longer uses.
/// </para>
/// <para>
/// IT THROWS WHEN IT GIVES UP, with the exception's own type, exactly as <c>File.ReadAllText</c>
/// does — a missing file included. So a converted test's failure mode is unchanged except for the
/// race: nothing here loosens an assertion or turns an unreadable file into an empty string.
/// </para>
/// <para>
/// IT DOES NOT SEE THE CHANNEL LOCK, and must not. <c>ChannelFile_Lock</c> is an advisory lock
/// DIRECTORY beside the file, not an OS handle on it, so a test that holds that lock to prove the
/// engine waits still reads the same bytes through this reader as through <c>File.ReadAllText</c>.
/// </para>
/// </summary>
internal static class TestFile_Reader
{
    public static string Read_AllText(string filePath)
    {
        return Tolerant_FileReader.Read_AllText(filePath);
    }
}
