using System.Security.Cryptography;
using System.Text;

namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// ONE NUMBER OVER THE TEXT THE SESSIONS WILL ACTUALLY READ — the role protocols, the hooks, the
/// channel helper and the plugin manifest — so "is the installed kit the kit this host was built
/// with" can be answered by COMPARING FILES rather than by trusting a commit id.
///
/// <para>
/// WHY (VPS, 2026-09-07): a stage that touched no file under <c>kit/</c> still moved the checkout's
/// HEAD, the CLI records the marketplace repository's HEAD at install time as
/// <c>gitCommitSha</c>, and the startup check compared that against the commit the host was built
/// from. The two differed, the verdict was ContentMismatch, no session was allowed to start, and
/// the kit was byte-for-byte identical. The commit is a PROXY for the content; when the content
/// itself can be read, the proxy has no vote.
/// </para>
/// <para>
/// THE DIGEST RULE IS <c>kit/install.sh</c>'S: md5 per file, which that script already uses to
/// decide whether a copy is needed ("every copy is content-compared (md5)"). Same rule, so the
/// installer and the verifier can never disagree about whether two trees are the same. The path is
/// digested with the bytes, so moving a file is a change; separators are normalised to '/' so a
/// Windows build and a Linux daemon compute the same number for the same tree.
/// </para>
/// <para>
/// NULL MEANS "I COULD NOT ANSWER", never "they differ". A folder that is missing the part that
/// matters most leaves the question open, and an open question is reported by the caller rather
/// than converted into a refusal here — the same rule <see cref="PluginVersion_Verifier"/> already
/// states for an unstamped build.
/// </para>
/// </summary>
public static class KitContent_Digest
{
    /// <summary>
    /// What a session reads, in the order they are digested. <c>skills</c> is the role protocols and
    /// is REQUIRED — a tree without it is not a kit and the digest refuses to answer for it.
    /// Everything else is optional, because a kit may legitimately ship without hooks.
    /// </summary>
    public const string REQUIRED_FOLDER = "skills";

    public static readonly IReadOnlyList<string> DIGESTED_FOLDERS = ["skills", "hooks", "bin"];

    /// <summary>Digested as well, because the version the whole check turns on is written in it.</summary>
    public static readonly IReadOnlyList<string> DIGESTED_FILES = [Path.Combine(".claude-plugin", "plugin.json")];

    /// <summary>Null when this folder is not a readable kit — the question is left open, not answered "no".</summary>
    public static string? Compute_OrNull(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return null;

        if (!Directory.Exists(Path.Combine(folder, REQUIRED_FOLDER)))
            return null;

        try
        {
            List<(string RelativePath, string FullPath)> files = [];

            foreach (var subfolder in DIGESTED_FOLDERS)
            {
                var root = Path.Combine(folder, subfolder);

                if (!Directory.Exists(root))
                    continue;

                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    files.Add((Normalise(Path.GetRelativePath(folder, file)), file));
            }

            foreach (var relative in DIGESTED_FILES)
            {
                var file = Path.Combine(folder, relative);

                if (File.Exists(file))
                    files.Add((Normalise(relative), file));
            }

            if (files.Count == 0)
                return null;

            // ORDINAL, and sorted: Directory.EnumerateFiles gives no order guarantee, and a
            // culture-aware sort orders '-' and '_' differently per machine — either would make the
            // same tree digest differently on the build host and on the VPS, which is precisely the
            // false mismatch this class exists to end.
            files.Sort((left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));

            var accumulated = new StringBuilder();

            foreach (var (relativePath, fullPath) in files)
                accumulated.Append(relativePath).Append('\n').Append(Hash_File(fullPath)).Append('\n');

            return Hash_Text(accumulated.ToString());
        }
        catch
        {
            // Unreadable is not "different". Swallowed here and reported as null for the same reason
            // the reader distinguishes absence from illegibility: the two must not collapse.
            return null;
        }
    }

    /// <summary>
    /// True/false when both trees could be read, null when either could not. The tri-state is the
    /// whole contract: a caller must be able to tell "identical" from "cannot tell".
    /// </summary>
    public static bool? Same_Content(string? leftFolder, string? rightFolder)
    {
        var left = Compute_OrNull(leftFolder);
        var right = Compute_OrNull(rightFolder);

        if (left == null || right == null)
            return null;

        return string.Equals(left, right, StringComparison.Ordinal);
    }

    static string Normalise(string relativePath)
    {
        return relativePath.Replace('\\', '/');
    }

    static string Hash_File(string file)
    {
        using var stream = File.OpenRead(file);

        return Convert.ToHexStringLower(MD5.HashData(stream));
    }

    static string Hash_Text(string text)
    {
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
