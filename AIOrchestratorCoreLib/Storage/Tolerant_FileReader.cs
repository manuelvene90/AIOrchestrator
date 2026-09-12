namespace AIOrchestratorCoreLib.Storage;

/// <summary>
/// Reads a text file that <see cref="Atomic_FileWriter"/> may be replacing at this instant, and
/// RETRIES rather than answering wrongly.
///
/// <para>
/// WHY, MEASURED (2026-09-11, the first Windows run of this suite): Windows honours FileShare and
/// Linux does not, so the atomic writer's rename and an ordinary read collide here and nowhere else.
/// <c>File.ReadAllText</c> opens with <c>FileShare.Read</c>, which forbids the rename; the rename in
/// turn leaves the target briefly unopenable. Either way the loser gets
/// <c>IOException: The process cannot access the file … because it is being used by another
/// process</c>. On Linux the same pair simply reads the old bytes, which is why the fork never saw
/// it. Across five runs of the print-runner suite it landed on three DIFFERENT tests — it is the
/// mechanism that is racy, never the test that caught it.
/// </para>
/// <para>
/// TWO HALVES, and both are needed. <c>FileShare.Delete</c> is what lets the writer's rename proceed
/// while this read is open — without it this reader would merely move the failure onto the writer.
/// The retry covers the other direction: the window in which the target is already delete-pending
/// and cannot be opened at all. The writer holds the file for a rename, not a computation, so a few
/// short backoffs are the whole of it.
/// </para>
/// <para>
/// IT THROWS WHEN IT GIVES UP, and that is the difference from <see cref="Safe_FileReader"/>: a
/// caller whose file must parse (the print session's identity, say) needs the exception, because an
/// empty string there is a silent wrong answer that reads as "no session". Callers that genuinely
/// want a default on failure keep their swallow and put it OUTSIDE this call.
/// </para>
/// </summary>
public static class Tolerant_FileReader
{
    /// <summary>Enough to outlast a rename by a wide margin, short enough that a real lock still fails fast (~200 ms total).</summary>
    public const int ATTEMPTS = 5;

    /// <summary>The backoff grows by this much per attempt: 20, 40, 60, 80 ms.</summary>
    public const int BACKOFF_STEP_MILLISECONDS = 20;

    /// <summary>
    /// The file's text. Throws the LAST <see cref="IOException"/> when every attempt lost the race —
    /// the caller decides what that means. A missing file throws too, exactly as
    /// <c>File.ReadAllText</c> does: absence is the caller's question, not this one's.
    /// </summary>
    public static string Read_AllText(string filePath)
    {
        IOException? last = null;

        for (var attempt = 0; attempt < ATTEMPTS; attempt++)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);

                return reader.ReadToEnd();
            }
            catch (FileNotFoundException)
            {
                // NOT RETRIED, and deliberately: a file that is not there is an answer, not a race,
                // and retrying it would spend 200 ms on every absent-file probe the tick makes.
                throw;
            }
            catch (DirectoryNotFoundException)
            {
                throw;
            }
            catch (IOException e)
            {
                last = e;

                if (attempt < ATTEMPTS - 1)
                    Thread.Sleep(BACKOFF_STEP_MILLISECONDS * (attempt + 1));
            }
        }

        throw last!;
    }
}
