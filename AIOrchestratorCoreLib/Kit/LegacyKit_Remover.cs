namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// REMOVES THE KIT THIS APP USED TO INSTALL BY HAND, and reports what it could not remove.
///
/// This is not tidying. MEASURED on CLI 2.1.263: when ~/.claude/commands/supervisor.md and the
/// plugin's supervisor skill both answer to /supervisor, THE LOCAL FILE WINS — a session reads the
/// stale protocol and nothing says so. On every machine that upgrades to the plugin, the old files
/// are still sitting there, so without this the whole stage delivers the OPPOSITE of what it is for:
/// `claude plugin list` would report 1.0.0 while every session ran the text from whichever build
/// last copied it. That is decisions 18 and 23 again, in their worst form yet, because this time a
/// version number would be actively lying.
///
/// It touches ONLY the exact filenames this app shipped — the six role protocols, the append helper
/// and the seven hooks. Anything else in those folders belongs to the person whose machine it is and
/// is never touched. What it fails to move it RETURNS, so the caller refuses rather than assuming.
///
/// IT MOVES ASIDE, IT DOES NOT DELETE, and that is the whole of its safety. The names it acts on are
/// GENERIC — `reviewer.md`, `solo.md`, `communicator.md` — so a file this app never wrote can carry
/// one, and unlinking it would destroy somebody's own work unrecoverably at an app start they did not
/// ask for. Renaming to <c>.aiorch-removed</c> breaks the shadow just as completely (a command is
/// resolved by its .md name, and that name is gone) while leaving every byte on disk, next to the
/// original, for whoever wants it back. Every move is logged with both names.
/// </summary>
public static class LegacyKit_Remover
{
    /// <summary>The commands this app installed into ~/.claude/commands, by the names it used.</summary>
    public static readonly IReadOnlyList<string> LEGACY_COMMAND_FILES =
    [
        "supervisor.md", "implementer.md", "reviewer.md", "solo.md",
        "general-supervisor.md", "communicator.md", "channel-append.sh",
    ];

    /// <summary>The hooks this app installed into ~/.claude/hooks, by the names it used.</summary>
    public static readonly IReadOnlyList<string> LEGACY_HOOK_FILES =
    [
        "supervisor-ledger-check.sh", "run-to-the-end-check.sh", "reviewer-readonly-check.sh",
        "supervisor-awaiting-answer-check.sh", "hook-log.sh", "hook-behaviour-check.sh",
        "watcher-behaviour-check.sh",
    ];

    public const string PROVENANCE_FILE_NAME = ".installed-by.txt";

    /// <summary>Appended to a file moved aside. Chosen so the .md extension no longer matches.</summary>
    public const string MOVED_ASIDE_SUFFIX = ".aiorch-removed";

    /// <summary>
    /// Moves aside what it can. Returns (movedAside, stillThere) — stillThere is every legacy file
    /// still sitting under its ORIGINAL name afterwards, whether because the move threw or because
    /// the file reappeared. A non-empty stillThere for a COMMAND is a shadow, and the caller must
    /// refuse on it.
    /// </summary>
    public static (IReadOnlyList<string> Removed, IReadOnlyList<string> StillThere) Remove(string claudeHomeFolder)
    {
        List<string> movedAside = [];
        List<string> stillThere = [];

        Sweep(Path.Combine(claudeHomeFolder, "commands"), LEGACY_COMMAND_FILES, movedAside, stillThere);
        Sweep(Path.Combine(claudeHomeFolder, "hooks"), LEGACY_HOOK_FILES, movedAside, stillThere);

        // The note the old installer dropped beside the commands it owned. Cosmetic, so a failure to
        // move it is not a reason to refuse anything — it goes to its own list and is dropped.
        Sweep(Path.Combine(claudeHomeFolder, "commands"), [PROVENANCE_FILE_NAME], movedAside, []);

        return (movedAside, stillThere);
    }

    /// <summary>
    /// Which of the six role words a stale local command would still answer for. Read separately from
    /// <see cref="Remove"/> so the check can be re-run after a removal that may have failed — the
    /// question "is a session going to read the old text" must be answered by looking, not by
    /// trusting that the delete worked.
    /// </summary>
    public static IReadOnlyList<string> Find_ShadowingCommands(string claudeHomeFolder)
    {
        var commands = Path.Combine(claudeHomeFolder, "commands");

        if (!Directory.Exists(commands))
            return [];

        return [.. LEGACY_COMMAND_FILES
            .Select(name => Path.Combine(commands, name))
            .Where(File.Exists)];
    }

    static void Sweep(string folder, IReadOnlyList<string> names, List<string> movedAside, List<string> stillThere)
    {
        if (!Directory.Exists(folder))
            return;

        foreach (var name in names)
        {
            var file = Path.Combine(folder, name);

            if (!File.Exists(file))
                continue;

            try
            {
                // overwrite: a second app start must not fail because the first one already put a
                // file of this name aside. The newer copy is the one worth keeping — it is the one
                // that was shadowing a moment ago.
                File.Move(file, file + MOVED_ASIDE_SUFFIX, overwrite: true);
                movedAside.Add(file);
            }
            catch
            {
                // Deliberately swallowed HERE and re-raised by the caller looking at what is left:
                // the exception type does not matter, only whether the file can still shadow.
            }

            if (File.Exists(file))
                stillThere.Add(file);
        }
    }
}
