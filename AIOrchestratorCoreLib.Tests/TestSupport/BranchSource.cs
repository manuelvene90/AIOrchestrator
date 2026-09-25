namespace AIOrchestratorCoreLib.Tests.TestSupport;

/// <summary>
/// READS A PRODUCTION FILE FROM THE BRANCH SOURCE for a scan test, walking up from the test binary. There
/// are already eight private copies of this walk in scan classes; this is the one a new scan takes instead
/// of writing a ninth (the code-conventions rule: share the helpers rather than add another copy).
///
/// <para>
/// SAY WHICH COPY YOU READ (CLAUDE.md decision 18): the branch source, never <c>bin/</c> or <c>obj/</c>.
/// And it REFUSES rather than returning empty when the file is not found (decision 20) — a scan that cannot
/// read its subject would otherwise certify the absence of whatever it looks for.
/// </para>
/// </summary>
internal static class BranchSource
{
    public static string Read(string fileName)
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var source = Directory.GetFiles(folder, fileName, SearchOption.AllDirectories).FirstOrDefault(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

            if (source != null)
                return File.ReadAllText(source);

            var parent = Directory.GetParent(folder);

            if (parent == null)
                break;

            folder = parent.FullName;
        }

        throw new Exception($"{fileName} not found walking up from {AppContext.BaseDirectory} — a scan that cannot read its subject must refuse to run, not pass");
    }

    /// <summary>
    /// The source with its comments removed, for a scan that asserts a name is NOT used: a doc that says
    /// "never <c>GitSnapshot_Reader</c>" is the rule written down, not a call.
    /// </summary>
    public static string Read_Code(string fileName)
    {
        var lines = Read(fileName).Split('\n')
            .Select(line => line.IndexOf("//", StringComparison.Ordinal) is var at and >= 0 ? line[..at] : line);

        return string.Join('\n', lines);
    }

    /// <summary>The brace-matched body that follows <paramref name="signatureMark"/>, or a refusal.</summary>
    public static string Extract_Method(string source, string signatureMark)
    {
        var at = source.IndexOf(signatureMark, StringComparison.Ordinal);

        if (at < 0)
            throw new Exception($"'{signatureMark}' is not in the source — a scan cannot prove anything about a method it cannot find");

        var open = source.IndexOf('{', at);
        var depth = 0;

        for (var i = open; open >= 0 && i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}' && --depth == 0)
                return source[open..(i + 1)];
        }

        throw new Exception($"no balanced body found for '{signatureMark}'");
    }
}
