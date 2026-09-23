using AIOrchestratorCoreLib.Storage;

namespace AIOrchestratorCoreLib.Bridge.Siblings;

/// <summary>
/// THE TWO MOVES OF A DERIVED FILE (spec 2026-09-23 §3.4): written when its text changes, removed when the
/// fact it stands for stops being true. Shared by <c>.siblings</c> and <c>ENDEAVOUR.md</c> so the "only when
/// the text changes" rule has one spelling — the tick reconciles both every two seconds, and a rewrite of an
/// unchanged world is disk churn for nothing.
///
/// <para>
/// BOTH THROW on an I/O failure, deliberately: the caller (<see cref="EndeavourArtefacts_Step"/>) turns each
/// throw into a line for the log naming the file, so a locked file costs one reconcile of one file and never
/// the tick.
/// </para>
/// </summary>
internal static class DerivedFile_Writer
{
    /// <summary>Writes <paramref name="text"/> atomically unless the file already holds exactly it; returns whether it wrote.</summary>
    public static bool Write_IfChanged(string filePath, string text)
    {
        if (File.Exists(filePath) && string.Equals(File.ReadAllText(filePath), text, StringComparison.Ordinal))
            return false;

        Atomic_FileWriter.Write_AllText(filePath, text);
        return true;
    }

    /// <summary>Removes the file if it is there; returns whether it removed one.</summary>
    public static bool Delete_IfPresent(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        File.Delete(filePath);
        return true;
    }
}
