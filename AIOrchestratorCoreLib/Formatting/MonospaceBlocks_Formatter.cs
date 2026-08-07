using System.Text;
using System.Text.RegularExpressions;

namespace AIOrchestratorCoreLib.Formatting;

/// <summary>
/// Makes ASCII mockups survive the trip to a phone. Agents draw layout options, tables and trees in
/// fenced ``` blocks; Telegram's default rendering uses a proportional font, which turns any such
/// drawing into noise. Sent as HTML with a &lt;pre&gt; block it arrives monospaced and aligned.
///
/// The same fences also mark the text that must NOT be translated by the Italian layer — a mockup
/// or a code snippet is not prose, and translating it would corrupt the very thing being shown.
/// </summary>
public static partial class MonospaceBlocks_Formatter
{
    [GeneratedRegex(@"```[^\n]*\n(.*?)```", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex FencedBlock_Regex();

    const string PLACEHOLDER_PREFIX = "⁣BLOCK";

    public static bool Has_Blocks(string text)
    {
        return FencedBlock_Regex().IsMatch(text);
    }

    /// <summary>
    /// Swaps fenced blocks for inert placeholders so surrounding prose can be translated while the
    /// blocks travel untouched. The placeholder uses an invisible separator, so a translator has
    /// nothing to "helpfully" reword.
    /// </summary>
    public static (string TextWithPlaceholders, IReadOnlyList<string> Blocks) Extract_Blocks(string text)
    {
        List<string> blocks = [];

        var replaced = FencedBlock_Regex().Replace(text, match =>
        {
            blocks.Add(match.Groups[1].Value);
            return $"{PLACEHOLDER_PREFIX}{blocks.Count - 1}⁣";
        });

        return (replaced, blocks);
    }

    public static string Restore_Blocks(string textWithPlaceholders, IReadOnlyList<string> blocks)
    {
        var restored = textWithPlaceholders;

        for (var i = 0; i < blocks.Count; i++)
            restored = restored.Replace($"{PLACEHOLDER_PREFIX}{i}⁣", $"```\n{blocks[i]}```");

        return restored;
    }

    /// <summary>
    /// Renders for Telegram's HTML parse mode: everything escaped, fenced blocks becoming &lt;pre&gt;
    /// so they keep a monospaced font and their alignment.
    /// </summary>
    public static string Build_Html(string text)
    {
        var html = new StringBuilder();
        var lastIndex = 0;

        foreach (Match match in FencedBlock_Regex().Matches(text))
        {
            html.Append(Escape_Html(text[lastIndex..match.Index]));
            html.Append("<pre>").Append(Escape_Html(match.Groups[1].Value.TrimEnd('\n'))).Append("</pre>");
            lastIndex = match.Index + match.Length;
        }

        html.Append(Escape_Html(text[lastIndex..]));
        return html.ToString();
    }

    static string Escape_Html(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
