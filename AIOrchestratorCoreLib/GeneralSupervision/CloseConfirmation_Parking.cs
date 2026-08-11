using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.GeneralSupervision;

/// <summary>
/// Where a close-orchestration request waits while the owner is asked to confirm it, and where it
/// goes once resolved.
///
/// It is a FOLDER rather than an in-memory flag, and that is the whole design. The request protocol
/// has NO "seen this one" mechanism: the only thing that stops a request executing again is the file
/// being gone, and the tick re-reads the folder every two seconds. An in-memory awaiting-set would
/// also die with the process, so the first tick after a restart would close the orchestration
/// unconfirmed — the exact failure this guard exists to prevent.
///
/// <see cref="OrchestrationRequests_Reader"/> enumerates ONE level of <c>*.json</c>, so a request
/// moved in here is invisible to it until something moves it back. That invisibility is the guard.
/// </summary>
public static class CloseConfirmation_Parking
{
    public const string AWAITING_FOLDER_NAME = "awaiting-owner";
    public const string RESOLVED_FOLDER_NAME = "resolved";

    /// <summary>
    /// How long an unanswered confirmation stays tappable. Not tidiness: a destructive confirmation
    /// must not outlive the situation that produced it. A close asked for last night should never
    /// execute on a stray tap tomorrow, when the work has moved on. Twelve hours covers a night
    /// without spanning two working contexts.
    /// </summary>
    public const int EXPIRY_HOURS = 12;

    public static string Get_AwaitingFolder(ISupervisionPaths paths)
    {
        return Path.Combine(paths.RequestsFolder, AWAITING_FOLDER_NAME);
    }

    public static string Get_ResolvedFolder(ISupervisionPaths paths)
    {
        return Path.Combine(paths.RequestsFolder, RESOLVED_FOLDER_NAME);
    }

    /// <summary>
    /// Moves a request out of the scanner's reach and returns its new path. The file CONTENT is
    /// untouched, so the parked file stays a readable record of exactly what was asked.
    /// </summary>
    public static string Park(ISupervisionPaths paths, string requestFilePath)
    {
        var awaitingFolder = Get_AwaitingFolder(paths);
        Directory.CreateDirectory(awaitingFolder);

        // UNIQUE, because the agent chooses the request's filename and the role commands say only
        // "any unique filename". Two orchestrations whose supervisors picked the same name would
        // collide here, and the loser was silently deleted below — so a tap could close orchestration
        // A while recording B's reason and requester. The original name is kept as a prefix so the
        // archive still reads like the file the agent wrote.
        var parkedPath = Path.Combine(
            awaitingFolder,
            $"{Path.GetFileNameWithoutExtension(requestFilePath)}-{Guid.NewGuid():N}{Path.GetExtension(requestFilePath)}");

        File.Move(requestFilePath, parkedPath);

        return parkedPath;
    }

    public static IReadOnlyList<string> Find_Parked(ISupervisionPaths paths)
    {
        var awaitingFolder = Get_AwaitingFolder(paths);

        if (!Directory.Exists(awaitingFolder))
            return [];

        try
        {
            return [.. Directory.EnumerateFiles(awaitingFolder, "*.json")];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Whether an unanswered request has sat past <see cref="EXPIRY_HOURS"/>. The clock is the
    /// file's own write time, which <see cref="File.Move"/> preserves — so it dates from when the
    /// agent ASKED, not from when the app happened to notice it.
    /// </summary>
    public static bool Is_Expired(string parkedFilePath, DateTime nowUtc)
    {
        // Explicitly, because File.GetLastWriteTimeUtc does NOT throw on a missing file — it returns
        // the year 1601, which would read as "expired long ago" and send this down the lapse path
        // for a request that is simply already resolved.
        if (!File.Exists(parkedFilePath))
            return false;

        try
        {
            return (nowUtc - File.GetLastWriteTimeUtc(parkedFilePath)).TotalHours >= EXPIRY_HOURS;
        }
        catch
        {
            // A file we cannot stat is not a licence to close an orchestration.
            return false;
        }
    }

    /// <summary>
    /// Files the request away under what was decided. This is the audit record the 2026-08-11 close
    /// did not leave: until now every processed request was deleted outright, so "who asked, and
    /// what did the owner say" was unanswerable minutes after the fact.
    /// </summary>
    public static void Archive(ISupervisionPaths paths, string parkedFilePath, string outcome)
    {
        var resolvedFolder = Get_ResolvedFolder(paths);
        Directory.CreateDirectory(resolvedFolder);

        var archivedPath = Path.Combine(resolvedFolder, $"{outcome}-{Path.GetFileName(parkedFilePath)}");

        if (File.Exists(archivedPath))
            File.Delete(archivedPath);

        File.Move(parkedFilePath, archivedPath);
    }
}
