using System.Security.Cryptography;
using System.Text;

namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// ONE NUMBER OVER THE TEXT THE SESSIONS WILL ACTUALLY READ — the role protocols, the hooks, the
/// channel helper, the channel grammar that helper reads, and the plugin manifest — so "is the
/// installed kit the kit this host was built with" can be answered by COMPARING FILES rather than
/// by trusting a commit id.
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
/// THIS IS A NARROWER SET THAN THE WHOLE KIT, DELIBERATELY, AND <see cref="SCOPE_DECLARATION"/> IS
/// THE ONE PLACE THAT SAYS WHICH (measured on the VPS, 2026-09-17). At 20:03:22 the daemon logged
/// *"content verified — the installed files are byte-identical to this build's kit"*; minutes later
/// <c>bash kit/install.sh</c>, against the same cache, answered *"the installed copy differs from
/// this checkout — REINSTALLING"*. Both were right about their own scope: <c>kit/install.sh</c> was
/// the only kit file changed between the build's commit and HEAD, it IS shipped in the plugin cache,
/// and this digest does not read it. The SENTENCE was the defect — the two components answered the
/// same question over different file sets and one of them said "byte-identical" about a subset.
/// Nothing was at risk that day (a setup script is not text a session loads), which is exactly why
/// it had to be fixed as a claim rather than as an incident: decision 12's shape.
/// </para>
/// <para>
/// WHY NOT SIMPLY THE WHOLE TREE, which would make the two agree. The two verdicts do not cost the
/// same. <c>install.sh</c>'s answer is "reinstall", which is cheap, correct for any drift, and stops
/// nobody; THIS answer is "refuse to spawn", which stops all work on the host. Widening this to the
/// whole tree would let a stale <c>README.md</c>, a <c>statusline/fixtures</c> test file or an
/// uncopied <c>install.sh</c> refuse every session — the 2026-09-07 incident again, reached from the
/// other side, and a false refusal is the more expensive of the two mistakes because the cheap
/// correct response to whole-tree drift already exists and already covers the whole tree. So the
/// scopes stay different, they are NAMED in the message (<c>KitAssets_Bootstrapper</c>) so the
/// stronger claim is never made again, and both installers restate this exact list beside their own
/// comparison, pinned by <c>KitVerifierScopeIsStatedInBothPlacesTests</c> — they cannot diverge
/// silently again.
/// </para>
/// <para>
/// THE ADMISSION TEST for this list is "would a SESSION read this file out of the cache". The
/// grammar passes it: <c>kit/bin/channel-append.sh</c> resolves it at
/// <c>../grammar/channel-grammar.json</c> relative to itself and refuses typed entries without it,
/// so a grammar drift is a session writing channel entries against a shape this build was not made
/// against — the precise class this digest exists to catch, and invisible to it until 2026-09-17.
/// <c>presets/</c> fails it (read by the HOST, and embedded in this assembly, so the host's copy
/// cannot go missing); <c>statusline/</c> fails it (installed from the build output by
/// <c>KitAssets_Installer</c>, not loaded from the cache by a session); <c>README.md</c>,
/// <c>install.sh</c>, <c>install.ps1</c> and <c>.claude-plugin/marketplace.json</c> fail it (read by
/// a human or by the CLI).
/// </para>
/// <para>
/// THE DIGEST RULE IS <c>kit/install.sh</c>'S: md5 per file, which that script already uses to
/// decide whether a copy is needed ("every copy is content-compared (md5)"). Same rule, so the
/// installer and the verifier can never disagree about whether two files are the same — only about
/// WHICH files they read, which is now stated. The path is digested with the bytes, so moving a file
/// is a change; separators are normalised to '/' so a Windows build and a Linux daemon compute the
/// same number for the same tree.
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

    public static readonly IReadOnlyList<string> DIGESTED_FOLDERS = ["skills", "hooks", "bin", "grammar"];

    /// <summary>Digested as well, because the version the whole check turns on is written in it.</summary>
    public static readonly IReadOnlyList<string> DIGESTED_FILES = [Path.Combine(".claude-plugin", "plugin.json")];

    /// <summary>
    /// THE SCOPE FOR A MACHINE, and the only statement of it anywhere: both installers carry this
    /// list verbatim beside their own whole-folder comparison, so a reader who sees the two
    /// components disagree can tell WHY from either place.
    /// <c>KitVerifierScopeIsStatedInBothPlacesTests</c> compares their copies to this, so adding a
    /// folder here and nowhere else is red.
    ///
    /// <para>
    /// SPACE-SEPARATED, and that is not cosmetic: <c>install.sh</c> word-splits its copy to walk the
    /// list, so a comma would become part of a path. The commas belong to
    /// <see cref="Describe_Scope"/>, which is the half a human reads.
    /// </para>
    /// </summary>
    public static string SCOPE_DECLARATION =>
        string.Join(' ', Scope_Paths());

    /// <summary>
    /// THE SCOPE FOR A PERSON — the log line and the refusal are built from this, so "OK" can never
    /// again read as a claim about files this host never opened. Derived from the same list, never
    /// typed out a second time (decision 12).
    /// </summary>
    public static string Describe_Scope()
    {
        return $"the files a session reads ({string.Join(", ", Scope_Paths())})";
    }

    static IEnumerable<string> Scope_Paths()
    {
        return DIGESTED_FOLDERS.Concat(DIGESTED_FILES.Select(file => file.Replace('\\', '/')));
    }

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
