using System.Reflection;

namespace AIOrchestratorCoreLib.Build;

/// <summary>
/// WHICH COMMIT THIS BINARY WAS BUILT FROM — the fact that turns "is the installed kit mine?" from a
/// version-number comparison into a content comparison.
///
/// <para>
/// WHY IT IS NEEDED AT ALL. Measured on 2026-09-07, CLI 2.1.263: <c>claude plugin update</c> compares
/// the VERSION STRING and nothing else. A commit that changes a role protocol without bumping
/// <c>kit/.claude-plugin/plugin.json</c> leaves the cached copy untouched and reports *"aiorch is
/// already at the latest version (1.0.0)"* — and <c>claude plugin marketplace update</c> first does
/// not help. So a host asserting <c>EXPECTED_VERSION</c> certifies a kit whose text it has never seen:
/// exactly the four-copies problem decision 18 was written about, one floor down. The installed record
/// carries <c>gitCommitSha</c> (yes, even for a local-directory marketplace — measured), so the honest
/// question is "is the installed copy the commit I was built from", and this is the other half of it.
/// </para>
/// <para>
/// BAKED IN AT BUILD TIME, unlike <see cref="BuildStamp_Reader"/> next door, and for the opposite
/// reason: that one answers "which binary am I" and must stay true without regeneration, so it reads
/// the file system; this one answers "which SOURCE am I", which the file system cannot know. The
/// stamping is an MSBuild target that runs <c>git rev-parse HEAD</c> into <c>SourceRevisionId</c>; the
/// SDK then appends it to <c>AssemblyInformationalVersion</c> after a <c>+</c>.
/// </para>
/// <para>
/// NULL IS AN ANSWER AND IT IS NOT "OK". A build made where git is unavailable — a tarball, an offline
/// image — carries no sha, and this returns null. The caller must then SAY that the content check
/// could not run rather than treat it as a pass: decision 21, a component that cannot evaluate its
/// predicate says so instead of granting silent consent.
/// </para>
/// </summary>
public static class BuildCommit_Reader
{
    /// <summary>Short enough to be typed by a human, long enough not to collide by accident.</summary>
    public const int MINIMUM_SHA_LENGTH = 7;

    /// <summary>
    /// The commit this library was compiled from, or null when the build carried no stamp.
    ///
    /// Read off THIS assembly rather than the entry assembly on purpose: the kit check lives here, the
    /// kit is in this repository, and the entry assembly is a different question (a test host, the WPF
    /// app, the daemon) that would answer with whatever happened to start the process.
    /// </summary>
    public static string? Read_RunningBuildCommit_OrNull()
    {
        var informational = typeof(BuildCommit_Reader).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return Extract_CommitSha_OrNull(informational);
    }

    /// <summary>
    /// Pulls the sha out of <c>1.2.3+abcdef0…</c>. Pure, so every shape below is asserted rather than
    /// argued: no stamp at all, a plain version, a pre-release tag with no sha, and a <c>+</c> carrying
    /// something that is not a sha.
    ///
    /// <para>
    /// The LAST <c>+</c> wins, because semver build metadata is everything after the first one and a
    /// project may already put a label there; a non-hex remainder is refused rather than returned,
    /// since a "sha" that is really a build label would compare unequal to every real sha and turn the
    /// content check into a permanent refusal to spawn.
    /// </para>
    /// </summary>
    public static string? Extract_CommitSha_OrNull(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
            return null;

        var plus = informationalVersion.LastIndexOf('+');

        if (plus < 0 || plus == informationalVersion.Length - 1)
            return null;

        var candidate = informationalVersion[(plus + 1)..].Trim();

        if (candidate.Length < MINIMUM_SHA_LENGTH || !candidate.All(Uri.IsHexDigit))
            return null;

        return candidate.ToLowerInvariant();
    }

    /// <summary>
    /// Whether two commit ids name the same commit, tolerating an abbreviation on either side.
    ///
    /// Both sides are full forty-character shas today — the installed record's and the build stamp's —
    /// so this could be an equality check. It is a prefix check because the day one of them is
    /// shortened (a hand-written stamp, a CI that abbreviates) the failure of an equality check would
    /// be a host that refuses to spawn anything, with a message saying two identical-looking shas
    /// differ.
    /// </summary>
    public static bool Names_TheSameCommit(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        var shorter = left.Length <= right.Length ? left : right;
        var longer = left.Length <= right.Length ? right : left;

        if (shorter.Length < MINIMUM_SHA_LENGTH)
            return false;

        return longer.StartsWith(shorter, StringComparison.OrdinalIgnoreCase);
    }
}
