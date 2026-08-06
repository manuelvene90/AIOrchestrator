using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.GeneralSupervision;

/// <summary>
/// Ensures the general supervisor's home (folder + seeded channel + requests folder + persistent
/// knowledge file) exists. The general folder is the general supervisor's PERMANENT working
/// directory on every machine: its CLAUDE.md auto-loads into every session as its long-term,
/// machine-portable memory (the agent maintains it itself).
/// </summary>
public static class GeneralChannel_Initializer
{
    public static void Ensure_Exists(ISupervisionPaths paths)
    {
        Directory.CreateDirectory(paths.GeneralFolder);
        Directory.CreateDirectory(paths.RequestsFolder);

        if (!File.Exists(paths.GeneralChannelFile))
            File.WriteAllText(paths.GeneralChannelFile, ChannelSeed_Builder.Build_GeneralChannelSeed());

        var knowledgeFile = Path.Combine(paths.GeneralFolder, "CLAUDE.md");

        if (!File.Exists(knowledgeFile))
            File.WriteAllText(knowledgeFile, Build_KnowledgeSeed());
    }

    static string Build_KnowledgeSeed()
    {
        return
            "# General Supervisor — persistent knowledge\n" +
            "\n" +
            "This folder is the general supervisor's permanent home: every general supervisor session runs\n" +
            "here, and this file loads automatically into each one. It is YOUR long-term memory, portable\n" +
            "across machines by design — keep it current yourself (Write/Edit) as you learn.\n" +
            "\n" +
            "What belongs here:\n" +
            "- The repo map: each configured repo (see ../config.json), what it IS, and the colloquial names\n" +
            "  the owner uses for it (\"the CRM\", \"the arb thing\", ...).\n" +
            "- Owner preferences and standing instructions about orchestration management.\n" +
            "- Anything that lets a future session act without re-learning.\n" +
            "\n" +
            "FIRST RUN ON A NEW MACHINE: this file is a bare seed. Say so in your greeting and ask the owner\n" +
            "where to learn the repo landscape (they may point you at machine-local files). Record what you\n" +
            "learn HERE — never assume machine-local files exist on other machines.\n";
    }
}
