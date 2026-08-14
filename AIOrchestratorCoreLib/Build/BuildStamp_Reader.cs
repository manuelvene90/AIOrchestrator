using System.Reflection;

namespace AIOrchestratorCoreLib.Build;

/// <summary>
/// ANSWERS "AM I RUNNING MY CHANGE?" WITHOUT AN INVESTIGATION — one line, always on screen.
///
/// This system keeps every file three times (branch source, build output, installed copy) and the
/// app itself can be launched from a copy of its own output. On 2026-08-14 the owner spent an
/// evening reading fixes into an app started from "bin\Debug - Copia" and built at 18:35, while
/// every fix under discussion had landed in bin\Debug hours later. Nothing on screen contradicted
/// them, and the only way to find out was to go and stat the binary.
///
/// Two facts, because either alone is uninformative: WHERE the running binary lives — the folder
/// name is what exposes a copy — and WHEN it was built. Both are read from the file system rather
/// than baked in at compile time, so nothing has to be regenerated for the answer to stay true.
/// </summary>
public static class BuildStamp_Reader
{
    const string UNKNOWN_DESCRIPTION = "build unknown — could not read the running binary";

    /// <summary>
    /// The stamp for the binary this process is actually running, which is the only copy whose
    /// identity is ever in doubt.
    /// </summary>
    public static string Describe_RunningApp()
    {
        var assemblyPath = Assembly.GetEntryAssembly()?.Location;

        return string.IsNullOrEmpty(assemblyPath)
            ? UNKNOWN_DESCRIPTION
            : Describe(assemblyPath, DateTime.Now);
    }

    /// <summary>
    /// <paramref name="nowLocal"/> is passed in rather than read here so the same-day rule is
    /// testable: a build from today shows the time alone (the common case, and the one the owner
    /// checks against a build they just ran), while any other day carries its date, so a week-old
    /// copy cannot read as this evening's.
    /// </summary>
    public static string Describe(string assemblyFilePath, DateTime nowLocal)
    {
        DateTime builtLocal;

        try
        {
            if (!File.Exists(assemblyFilePath))
                return UNKNOWN_DESCRIPTION;

            builtLocal = File.GetLastWriteTime(assemblyFilePath);
        }
        catch
        {
            // Saying nothing and saying "fresh" are different answers, and a blank reads as the
            // second one. Same rule the hooks follow when they cannot evaluate their predicate.
            return UNKNOWN_DESCRIPTION;
        }

        var folderName = Path.GetFileName(Path.GetDirectoryName(assemblyFilePath) ?? "");

        var whenText = builtLocal.Date == nowLocal.Date
            ? builtLocal.ToString("HH:mm")
            : builtLocal.ToString("d MMM HH:mm");

        return folderName.Length == 0
            ? $"build {whenText}"
            : $"build {whenText} · {folderName}";
    }
}
