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

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }
}
