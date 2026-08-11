namespace AIOrchestratorCoreLib.Storage;

/// <summary>
/// Replaces a file's contents by filling a sibling temp file first and renaming it over the target,
/// so an interrupted write leaves the ORIGINAL file intact instead of a truncated one.
/// <para>
/// Why this exists: <c>File.WriteAllText</c> opens with <c>FileMode.Create</c> — it truncates the
/// target to zero bytes and only then starts writing. On 2026-08-11 the disk hit 0 % free while
/// this app was rewriting its cursor file ~30 times a minute; every one of those writes was one
/// interruption away from destroying the file rather than merely failing to update it. A rename
/// cannot do that: the target is either the old file or the new one, never a mix.
/// </para>
/// <para>
/// Scope, stated honestly: this protects against a FAILED or INTERRUPTED write (full disk, crash,
/// process kill). It does not fsync, so a power loss can still lose a rename the OS had only
/// cached — that hazard is out of proportion to these files.
/// </para>
/// </summary>
public static class Atomic_FileWriter
{
    /// <summary>Marks the temp file for the human who finds one after a hard kill.</summary>
    public const string TEMP_FILE_SUFFIX = ".tmp";

    /// <summary>
    /// Writes <paramref name="contents"/> to <paramref name="filePath"/>, creating the folder if
    /// needed. Throws whatever the underlying write threw — the caller decides what a failed write
    /// means; this only guarantees the target was not damaged by the attempt.
    /// </summary>
    public static void Write_AllText(string filePath, string contents)
    {
        var folder = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        // The unique middle keeps two writers of the same target from filling one temp file.
        var tempFilePath = $"{filePath}.{Guid.NewGuid():N}{TEMP_FILE_SUFFIX}";

        try
        {
            File.WriteAllText(tempFilePath, contents);

            // Same folder, therefore same volume: the rename is a directory operation, not a copy.
            File.Move(tempFilePath, filePath, overwrite: true);
        }
        catch
        {
            Delete_BestEffort(tempFilePath);
            throw;
        }
    }

    static void Delete_BestEffort(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch
        {
            // Cleaning up debris must never replace the original exception, which is the one that
            // says WHY the write failed. A stray .tmp beside the file is a diagnostic, not damage.
        }
    }
}
