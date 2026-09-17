namespace AIOrchestratorCoreLib.Tests;

/// <summary>
/// The repo's own source, found from the test assembly's location. Scan tests read the SOURCE, which
/// is the point: they assert about code shape, not about behaviour, and there is nothing to run.
/// A walk that cannot find the tree THROWS rather than returning nothing — decision 20: a harness
/// that cannot find what it tests must refuse to run, never certify an absence it never looked at.
/// </summary>
public static class SourceTree
{
    public static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AIOrchestrator.slnx")))
                directory = directory.Parent;

            return directory?.FullName
                ?? throw new Exception($"Could not find the repo root above '{AppContext.BaseDirectory}' — this scan cannot judge code it has not read.");
        }
    }

    public static IEnumerable<string> EnumerateCSharp(string relativeFolder)
    {
        var folder = Path.Combine(Root, relativeFolder);

        if (!Directory.Exists(folder))
            throw new Exception($"'{folder}' does not exist — this scan cannot judge code it has not read.");

        foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            yield return file;
        }
    }

    /// <summary>
    /// SAY WHICH COPY YOU READ (CLAUDE.md decision 18): the BRANCH SOURCE of one file, by name, found
    /// under <see cref="Root"/> and never in a <c>bin</c> or <c>obj</c> folder. It THROWS when the file
    /// is not there rather than answering empty — decision 20: a scan that cannot find what it tests
    /// must refuse to run, because nothing-is-ALLOW is how a harness certifies an absence it never
    /// looked at.
    /// </summary>
    public static string Read_Source(string fileName)
    {
        foreach (var file in Directory.EnumerateFiles(Root, fileName, SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            return File.ReadAllText(file);
        }

        throw new Exception($"'{fileName}' is not under '{Root}' — this scan cannot judge code it has not read.");
    }

    /// <summary>
    /// The braced body of the method whose declaration contains <paramref name="signatureMark"/>,
    /// extracted from <paramref name="fileName"/> by matching braces.
    ///
    /// <para>
    /// A body shorter than <see cref="PLAUSIBLE_BODY_FLOOR"/> characters is a brace-matching failure,
    /// not a method, and it THROWS: an extraction that silently returned an empty body would make
    /// every <c>DoesNotContain</c> assertion above it pass for the wrong reason, which is the one
    /// failure mode a source scan has. A signature that is not in the file throws for the same reason
    /// (decision 20).
    /// </para>
    /// </summary>
    public static string Extract_Method(string fileName, string signatureMark)
    {
        var source = Read_Source(fileName);

        var at = source.IndexOf(signatureMark, StringComparison.Ordinal);

        if (at < 0)
            throw new Exception($"'{signatureMark}' is not in {fileName} — this scan cannot prove anything about a method it cannot find.");

        var open = source.IndexOf('{', at);

        if (open < 0)
            throw new Exception($"no body found for '{signatureMark}' in {fileName}.");

        var depth = 0;

        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}' && --depth == 0)
            {
                var body = source[open..(i + 1)];

                if (body.Length < PLAUSIBLE_BODY_FLOOR)
                    throw new Exception($"the body extracted for '{signatureMark}' is {body.Length} characters — that is a brace-matching failure, not a method.");

                return body;
            }
        }

        throw new Exception($"unbalanced braces walking the body of '{signatureMark}' in {fileName}.");
    }

    /// <summary>A body shorter than this is an extraction that went wrong, not a method.</summary>
    const int PLAUSIBLE_BODY_FLOOR = 200;
}
