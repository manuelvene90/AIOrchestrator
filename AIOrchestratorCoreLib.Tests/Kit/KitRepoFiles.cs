using AIOrchestratorCoreLib.Running;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// Finds a file or folder in the repo from the test binary's own output folder. ONE copy of the
/// walk-up that six Kit tests each carried privately — they all had to change together when the kit
/// moved from kit/commands/&lt;role&gt;.md to kit/skills/&lt;role&gt;/SKILL.md, which is the argument for
/// having one.
///
/// Returns null rather than a guess. Every caller turns that null into a refusal to run, because a
/// content test that found no content passes by finding nothing.
/// </summary>
public static class KitRepoFiles
{
    const int MAX_DEPTH = 8;

    public static string? Find(string relativePath)
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < MAX_DEPTH; depth++)
        {
            var candidate = Path.Combine(folder, relativePath);

            if (File.Exists(candidate) || Directory.Exists(candidate))
                return candidate;

            var parent = Directory.GetParent(folder);

            if (parent == null)
                break;

            folder = parent.FullName;
        }

        return null;
    }

    /// <summary>The protocol file of one role, at its plugin location.</summary>
    public static string? Find_RoleProtocol(string role)
    {
        return Find(Path.Combine("kit", "skills", role, "SKILL.md"));
    }

    /// <summary>
    /// Every ROLE protocol in the kit, as (role, path) — the role is the FOLDER now, not the filename.
    ///
    /// A role is a folder whose name is the slash word `SessionRole_Names.Build_RoleCommand` composes
    /// (`supervisor`, `implementer`, …, `general-supervisor`), so the six the app can spawn are the six
    /// this returns. `kit/skills/` also ships HOUSE skills — `subagents` since stage 1f — that a session
    /// loads by choice, which is the opposite of a role: no hook forbids entering them, no watcher
    /// reference sits beside them, and `disable-model-invocation` must stay OFF. Enumerating the folder
    /// blindly counted them as roles and failed two tests the day the first one landed (2026-09-07).
    /// </summary>
    public static IReadOnlyList<(string Role, string Path)> Find_AllRoleProtocols()
    {
        var skills = Find(Path.Combine("kit", "skills"));

        if (skills == null)
            return [];

        var roleFolders = SessionRole_Names.ALL
            .Select(role => SessionRole_Names.Build_RoleCommand(role, "orch", "member").Split(' ')[0].TrimStart('/'))
            .ToHashSet(StringComparer.Ordinal);

        return [.. Directory.GetDirectories(skills)
            .Select(folder => (Role: System.IO.Path.GetFileName(folder), Path: System.IO.Path.Combine(folder, "SKILL.md")))
            .Where(entry => roleFolders.Contains(entry.Role) && File.Exists(entry.Path))
            .OrderBy(entry => entry.Role)];
    }

    /// <summary>The house skills beside the roles — every `kit/skills/*/SKILL.md` that is NOT a role.</summary>
    public static IReadOnlyList<(string Skill, string Path)> Find_AllHouseSkills()
    {
        var skills = Find(Path.Combine("kit", "skills"));

        if (skills == null)
            return [];

        var roles = Find_AllRoleProtocols().Select(entry => entry.Role).ToHashSet(StringComparer.Ordinal);

        return [.. Directory.GetDirectories(skills)
            .Select(folder => (Skill: System.IO.Path.GetFileName(folder), Path: System.IO.Path.Combine(folder, "SKILL.md")))
            .Where(entry => !roles.Contains(entry.Skill) && File.Exists(entry.Path))
            .OrderBy(entry => entry.Skill)];
    }
}
