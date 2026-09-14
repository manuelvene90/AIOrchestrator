namespace AIOrchestratorCoreLib.Running.TurnLiveness;

/// <summary>
/// When a print turn's Claude Code session last WROTE its transcript — the strongest sign of life
/// the silence brake has, because the CLI appends a JSON line for every model message and every
/// tool result, whatever the turn prints (or does not print) on stdout.
///
/// <para>
/// WHERE THE FILES ARE (layout seen on this machine 2026-09-11, CLI in use that day):
///   <c>&lt;claudeHome&gt;/projects/&lt;project-slug&gt;/&lt;sessionId&gt;.jsonl</c>                    the turn itself
///   <c>&lt;claudeHome&gt;/projects/&lt;project-slug&gt;/&lt;sessionId&gt;/subagents/agent-*.jsonl</c>  each sub-agent it fans out to
/// </para>
/// <para>
/// BOTH ARE READ, AND THE NEWEST WINS. A turn whose parent is blocked waiting for a sub-agent writes
/// nothing to its own file for as long as the sub-agent works, while the sub-agent's file keeps
/// growing. Reading only the parent would call a turn silent at exactly the moment it is doing the
/// most work — and fan-out is the implementer's default (decision 16), so that is the normal case.
/// </para>
/// <para>
/// THE SLUG IS FOUND, NEVER COMPUTED. The CLI derives it from the working directory and the rule
/// differs per OS (separators, drive letters, dots); recomputing it here would be a second copy of a
/// rule we do not own, and it would drift silently — a wrong slug reads as "no transcript", which is
/// the answer that lets a brake fire. So the file is located by session id across every
/// <c>projects/*</c> folder. That also covers a session found in TWO project folders (a resumed
/// session whose working directory changed): every copy counts, and the newest wins.
/// </para>
/// <para>
/// NULL MEANS "THIS SIGNAL SAYS NOTHING", never "the turn is dead". A turn that has just started may
/// not have created its file yet, and a folder may be unreadable for a moment; the brake combines
/// this with other signals, and it must never throw into the turn loop. File CONTENTS are never
/// read — only timestamps — so neither a half-written line nor a private transcript is ever parsed.
/// </para>
/// <para>
/// WHICH CLAUDE HOME. Use <see cref="Resolve_ClaudeHome"/>, NOT <c>IHostOptions.ClaudeHome</c>: that
/// one is INSTALL-ONLY (<c>--claude-home</c> / <c>AIORCH_CLAUDE_HOME</c>, where the kit is copied),
/// while the CLI reads and writes its own folder from <c>CLAUDE_CONFIG_DIR</c> or <c>~/.claude</c>
/// (README-daemon.md, measured 2026-09-06). The two coincide by default and diverge exactly when a
/// daemon is pointed elsewhere — and there a transcript looked for in the install folder is never
/// found, which is the quiet failure again.
/// </para>
/// </summary>
public static class TranscriptActivity_Reader
{
    /// <summary>The variable the CLI itself honours for its config folder.</summary>
    public const string CLAUDE_CONFIG_DIR_ENV = "CLAUDE_CONFIG_DIR";

    public const string PROJECTS_FOLDER = "projects";
    public const string TRANSCRIPT_EXTENSION = ".jsonl";

    /// <summary>
    /// How deep under <c>&lt;sessionId&gt;/</c> the sub-agent search goes. Today the files sit at
    /// depth 1 (<c>subagents/</c>); the bound leaves room for a layout change without letting a
    /// surprising tree turn a 10-second poll into a crawl.
    /// </summary>
    const int MAX_SUBAGENT_DEPTH = 8;

    /// <summary>The 36-character hyphenated GUID the app passes as <c>--session-id</c>/<c>--resume</c>.</summary>
    const int SESSION_ID_LENGTH = 36;

    /// <summary>
    /// The folder the CLI keeps its transcripts under: <c>CLAUDE_CONFIG_DIR</c> when set and
    /// non-blank — from <paramref name="environmentOverrides"/> first (the environment a caller hands
    /// the spawned <c>claude</c> process, which is the one that decides where it writes), then this
    /// process's environment — else <c>~/.claude</c>. A blank value is treated as unset: taken
    /// literally it would resolve against the current directory, which is never where the
    /// transcripts are, and a reader looking in the wrong place answers "silent".
    /// </summary>
    public static string Resolve_ClaudeHome(IReadOnlyDictionary<string, string>? environmentOverrides)
    {
        if (environmentOverrides != null
            && environmentOverrides.TryGetValue(CLAUDE_CONFIG_DIR_ENV, out var overridden)
            && !string.IsNullOrWhiteSpace(overridden))
            return overridden;

        var inherited = Environment.GetEnvironmentVariable(CLAUDE_CONFIG_DIR_ENV);

        if (!string.IsNullOrWhiteSpace(inherited))
            return inherited;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    }

    /// <summary>
    /// The newest last-write time (UTC) among <c>projects/*/&lt;sessionId&gt;.jsonl</c> and every
    /// <c>*.jsonl</c> anywhere under <c>projects/*/&lt;sessionId&gt;/</c>. Null when no such file
    /// exists yet, when the session id is not a GUID, or when nothing could be read.
    ///
    /// Cost: one directory listing of <c>projects/</c> plus two existence probes per project folder,
    /// and a bounded listing of the session's own folder — no file is opened. Polled every ~10–15 s
    /// per running turn, that is cheap even with dozens of project folders.
    /// </summary>
    public static DateTime? Read_LastWriteUtc_OrNull(string claudeHome, string sessionId)
    {
        return Read_Newest_OrNull(claudeHome, sessionId, includeMainTranscript: true);
    }

    /// <summary>
    /// The same reading over the SUB-AGENTS' transcripts only — what tells a member waiting on its own
    /// fan-out from a member repeating itself: the loop detector's repeated poll writes the main file,
    /// and only real work elsewhere writes these.
    /// </summary>
    public static DateTime? Read_SubAgentLastWriteUtc_OrNull(string claudeHome, string sessionId)
    {
        return Read_Newest_OrNull(claudeHome, sessionId, includeMainTranscript: false);
    }

    static DateTime? Read_Newest_OrNull(string claudeHome, string sessionId, bool includeMainTranscript)
    {
        // THE SESSION ID BECOMES A PATH SEGMENT, so it is validated before it touches one: only the
        // exact hyphenated GUID shape is accepted. Anything else — "../x", "a/b", "*", a padded or
        // braced GUID — returns null rather than a path built from it. Null is the safe direction
        // here: the brake reads it as "no evidence", never as a verdict.
        if (!Is_SessionIdShape(sessionId))
            return null;

        // A blank home would make Path.Combine resolve "projects" against the current directory,
        // reading some other tree's files as this session's. No home, no signal.
        if (string.IsNullOrWhiteSpace(claudeHome))
            return null;

        try
        {
            var projectsFolder = new DirectoryInfo(Path.Combine(claudeHome, PROJECTS_FOLDER));

            if (!projectsFolder.Exists)
                return null;

            DateTime? newest = null;

            foreach (var projectFolder in projectsFolder.EnumerateDirectories())
                newest = Latest_OrNull(newest, Read_ProjectFolder_OrNull(projectFolder.FullName, sessionId, includeMainTranscript));

            return newest;
        }
        catch
        {
            // projects/ vanished or became unreadable between the probe and the listing, or the
            // home is not a legal path. This signal then says nothing — the brake has others, and a
            // throw here would land in the turn loop.
            return null;
        }
    }

    /// <summary>
    /// The session's OWN transcript — <c>projects/*/&lt;sessionId&gt;.jsonl</c>, the newest if a cwd
    /// change left it in two project folders — for the loop detector, which reads the main agent's
    /// steps and nothing a sub-agent wrote. Null for an id that is not a session id, a blank home, or
    /// no such file yet; the same safe direction as <see cref="Read_LastWriteUtc_OrNull"/>.
    /// </summary>
    public static string? Find_MainTranscript_OrNull(string claudeHome, string sessionId)
    {
        if (!Is_SessionIdShape(sessionId) || string.IsNullOrWhiteSpace(claudeHome))
            return null;

        try
        {
            var projectsFolder = new DirectoryInfo(Path.Combine(claudeHome, PROJECTS_FOLDER));

            if (!projectsFolder.Exists)
                return null;

            FileInfo? newest = null;

            foreach (var projectFolder in projectsFolder.EnumerateDirectories())
            {
                var candidate = new FileInfo(Path.Combine(projectFolder.FullName, sessionId + TRANSCRIPT_EXTENSION));

                if (candidate.Exists && (newest == null || candidate.LastWriteTimeUtc > newest.LastWriteTimeUtc))
                    newest = candidate;
            }

            return newest?.FullName;
        }
        catch
        {
            // Same as the reader above: an unreadable projects/ says nothing, and must not throw into the turn loop.
            return null;
        }
    }

    /// <summary>
    /// One project folder's contribution. Its own try, so ONE unreadable sibling (another repo's
    /// folder with odd permissions) cannot blind the reading for the folder that holds this session.
    /// </summary>
    static DateTime? Read_ProjectFolder_OrNull(string projectFolder, string sessionId, bool includeMainTranscript)
    {
        try
        {
            DateTime? newest = null;

            // FileInfo, not File.GetLastWriteTimeUtc: the latter answers a MISSING file with
            // 1601-01-01 instead of failing, so a transcript deleted between the probe and the read
            // would come back as a very old "last write" — a confident wrong number. FileInfo reads
            // existence and timestamp from the same stat.
            var mainTranscript = new FileInfo(Path.Combine(projectFolder, sessionId + TRANSCRIPT_EXTENSION));

            if (includeMainTranscript && mainTranscript.Exists)
                newest = mainTranscript.LastWriteTimeUtc;

            var sessionFolder = new DirectoryInfo(Path.Combine(projectFolder, sessionId));

            if (!sessionFolder.Exists)
                return newest;

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = MAX_SUBAGENT_DEPTH,
                IgnoreInaccessible = true,

                // Symlinks are skipped so the search stays INSIDE the session's folder — a link out
                // of it (or back up to it) could otherwise walk an unrelated tree or loop. The CLI
                // writes sub-agent transcripts as plain files.
                AttributesToSkip = FileAttributes.ReparsePoint,
            };

            foreach (var subagentTranscript in sessionFolder.EnumerateFiles("*" + TRANSCRIPT_EXTENSION, options))
                newest = Latest_OrNull(newest, subagentTranscript.LastWriteTimeUtc);

            return newest;
        }
        catch
        {
            // This folder is unreadable right now; the others still speak.
            return null;
        }
    }

    static bool Is_SessionIdShape(string? sessionId)
    {
        // The length check comes first because Guid parsing tolerates surrounding whitespace, and
        // " <guid>" must not reach Path.Combine as a different file name.
        return sessionId != null
            && sessionId.Length == SESSION_ID_LENGTH
            && Guid.TryParseExact(sessionId, "D", out _);
    }

    static DateTime? Latest_OrNull(DateTime? held, DateTime? candidate)
    {
        if (candidate == null)
            return held;

        if (held == null || candidate.Value > held.Value)
            return candidate;

        return held;
    }
}
