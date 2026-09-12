namespace AIOrchestratorCoreLib.Storage;

/// <summary>
/// Reads a text file that another process may be writing at this instant, and answers the empty
/// string for anything that goes wrong — a missing file, a locked one, a torn read.
///
/// <para>
/// THE READ HALF OF <see cref="Atomic_FileWriter"/>, and it exists because the idiom had already been
/// written six times: the bridge engine, the usage reader, the transcript reader, the activity
/// describer, the tailer and the compactor each carry their own copy of these eight lines. That is the
/// shape every drifted-copy defect in this repository starts as, so a seventh copy is not the thing to
/// add. The existing six are left where they are — moving them is not this change's business — but
/// nothing new should grow one.
/// </para>
/// <para>
/// <c>FileShare.ReadWrite</c> is load-bearing: an agent's editor holds these files open, and the
/// default share mode turns an ordinary concurrent write into an exception on the reader's side.
/// </para>
/// </summary>
public static class Safe_FileReader
{
    public static string Read_AllText_OrEmpty(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return string.Empty;

            // THROUGH THE TOLERANT READER, and the swallow below stays as the LAST resort. Returning
            // empty for a file that merely lost a race with its own writer is a silent wrong answer:
            // on Windows the atomic writer's rename and a plain shared read collide, and the caller
            // cannot tell "the file said nothing" from "I could not open it for 0 ms". Retry first,
            // default only if it is still unreadable after that.
            return Tolerant_FileReader.Read_AllText(filePath);
        }
        catch
        {
            return string.Empty;
        }
    }
}
