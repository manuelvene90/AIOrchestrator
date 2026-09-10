using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE BUSY NOTICE IS NOT WRITTEN TO A SESSION THAT HAS ALREADY ANSWERED.
///
/// "[agent] the owner is waiting on you — answer them at your next boundary" is appended to the
/// owner channel when the owner's message has sat for OWNER_REPLY_GRACE_SECONDS with the session
/// mid-turn. It ignored `Answered`: on 2026-09-10 it told a solo that had replied a minute earlier
/// that the message was "still unanswered", the solo wrote another status line pointing at its own
/// reply, and that extra line is what overwrote the real answer in the suppressed memo. The owner
/// quoted the solo's complaint about exactly this the same morning.
///
/// Reaching the notice behaviourally costs 150 s of real time per case, so this pins the predicate
/// by reading the source — the same shape and the same honesty as
/// <see cref="AwaySuppressesAppAlertsScanTests"/>: it proves the gate is present and placed, not
/// that it fires.
/// </summary>
public class BusyNoticeRespectsAnAnswerScanTests
{
    const string ENGINE_FILE = "BridgeEngineModel.cs";
    const string NOTICE_SUBJECT = "the owner is waiting on you — answer them at your next boundary";

    [Fact]
    public void TheBusyNotice_IsGuardedByTheAnsweredFlag()
    {
        var body = Extract_Method("async Task Resolve_PendingOwnerReplies_Async");

        // The harness proves it found the right method before judging what is inside it.
        Assert.Contains("BusyNoticeWritten", body);

        var notice = body.IndexOf(NOTICE_SUBJECT, StringComparison.Ordinal);

        Assert.True(notice >= 0, "the busy notice is no longer written from the resolver — this test is reading a method it does not understand");

        // The predicate is the `if` immediately above the notice's append.
        var guardStart = body.LastIndexOf("if (", notice, StringComparison.Ordinal);

        Assert.True(guardStart >= 0, "no predicate guards the busy notice at all");

        var guard = body.Substring(guardStart, notice - guardStart);

        Assert.Contains("!pending.Answered", guard);
        Assert.Contains("!pending.BusyNoticeWritten", guard);
    }

    static string Extract_Method(string signatureMark)
    {
        var source = Read_EngineSource();

        var at = source.IndexOf(signatureMark, StringComparison.Ordinal);

        Assert.True(at >= 0, $"'{signatureMark}' is not in {ENGINE_FILE} — this scan cannot prove anything about a method it cannot find");

        var open = source.IndexOf('{', at);

        Assert.True(open >= 0, $"no body found for '{signatureMark}'");

        var depth = 0;

        for (var i = open; i < source.Length; i++)
        {
            // Line comments are skipped: the engine's prose is long and quotes braces.
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n')
                    i++;

                continue;
            }

            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;

                if (depth == 0)
                    return source.Substring(at, i - at + 1);
            }
        }

        throw new Exception($"the body of '{signatureMark}' never closed — {ENGINE_FILE} is not what this scan expects");
    }

    static string Read_EngineSource()
    {
        var folder = AppContext.BaseDirectory;

        while (folder != null)
        {
            var candidate = Path.Combine(folder, "AIOrchestratorCoreLib", "Bridge", "BridgeEngine", ENGINE_FILE);

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            folder = Path.GetDirectoryName(folder);
        }

        throw new Exception($"{ENGINE_FILE} was not found above '{AppContext.BaseDirectory}' — this scan needs the repository source");
    }
}
