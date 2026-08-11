namespace AIOrchestratorCoreLib.Storage;

/// <summary>
/// Free space on the volume a path lives on, for the guards that must stop writing BEFORE the disk
/// is the thing that breaks them. On 2026-08-11 the machine reached 0 % free and this app had no
/// preflight anywhere: it discovered the condition by having a write throw, in the one place
/// (logging) whose job is to record what went wrong.
/// </summary>
public static class Disk_Space
{
    /// <summary>
    /// Free bytes available on the volume holding <paramref name="path"/>, or null when the volume
    /// cannot be interrogated (a UNC share, a drive that went away). Null means UNKNOWN, and every
    /// caller must read it as "do not block" — a probe that fails is not evidence of a full disk,
    /// and refusing to write on it would silence the app for the wrong reason.
    /// </summary>
    public static long? Get_FreeBytes_OrNull(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));

            if (string.IsNullOrEmpty(root))
                return null;

            var drive = new DriveInfo(root);

            if (!drive.IsReady)
                return null;

            return drive.AvailableFreeSpace;
        }
        catch
        {
            // Deliberate: the null return IS this method's contract for "unknown", and a disk probe
            // that throws must never take down the caller it was added to protect.
            return null;
        }
    }
}
