using AIOrchestratorCoreLib.Storage;

namespace AIOrchestratorCoreLib.Running.StatePack;

/// <summary>
/// Replaces the pack — never appends. A pack is the memory of ONE turn; yesterday's pack under
/// today's would be exactly the growing file this stage exists to stop. Atomic, so a session that
/// boots while the bridge writes reads the old pack or the new one, never half of each.
/// </summary>
public static class StatePack_Writer
{
    public static void Write(string packFilePath, string text)
    {
        var folder = Path.GetDirectoryName(packFilePath);

        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        Atomic_FileWriter.Write_AllText(packFilePath, text);
    }
}
