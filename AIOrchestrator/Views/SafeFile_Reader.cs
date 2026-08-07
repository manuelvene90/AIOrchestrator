using System.IO;

namespace AIOrchestrator.Views;

/// <summary>
/// Reads files that agent sessions are actively appending to: shared-read access, and any
/// failure degrades to empty text — a UI refresh must never throw because of a mid-write file.
/// </summary>
public static class SafeFile_Reader
{
    public static string Read_Text_Safe(string filePath)
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
