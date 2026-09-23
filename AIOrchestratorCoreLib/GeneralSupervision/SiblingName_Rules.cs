namespace AIOrchestratorCoreLib.GeneralSupervision;

/// <summary>
/// The shape of a sibling's topic name: <c>&lt;code&gt; · &lt;2-4 words&gt;</c> (spec 2026-09-23 §4.1), the
/// owner's rule that <c>kit/skills/solo/SKILL.md</c> states as "EVERY TOPIC NAME STARTS WITH THE PLATFORM
/// CODE" (2026-08-19) — <c>AI-Orch · away mode loop</c>, <c>IS · portfolio picker</c> — so the owner reads
/// the topic list at a glance and speaks to the general supervisor in shorthand.
///
/// <para>
/// 2-4 WORDS AFTER THE CODE, NOT 1-4 (pre-flight ruling F, 2026-09-23). The plan allowed one word on the
/// claim that the skill's own examples include one-word names; they do not — the skill says "2-4 words, 3
/// is best" and every example has two or three. The spec is binding; a one-word name costs the solo one
/// re-drop with this sentence in hand.
/// </para>
/// <para>
/// THE CODE IS NOT CHECKED AGAINST THE PARENT'S, deliberately: "A SUB-PRODUCT KEEPS ITS OWN CODE", so a
/// Strategy Lab endeavour can legitimately have an <c>IS</c> sibling. Only its form is checked.
/// </para>
/// <para>
/// NO CONTROL CHARACTER, because the name is the last field of <c>.siblings</c>, which is tab-separated
/// and read line by line by bash: a tab shifts the fields and a newline splits one sibling into two.
/// </para>
/// </summary>
public static class SiblingName_Rules
{
    public const string SEPARATOR = " · ";
    public const int CODE_MAX_CHARS = 12;
    public const int MIN_WORDS = 2;
    public const int MAX_WORDS = 4;
    public const int NAME_MAX_CHARS = 64;

    /// <summary>Null for a legal name; otherwise one sentence naming the rule the name breaks.</summary>
    public static string? Describe_Refusal_OrNull(string name)
    {
        if (name.Any(character => character < 0x20))
            return "the name contains a tab, a newline or another control character — it must be one plain line";

        if (name.Length > NAME_MAX_CHARS)
            return $"the name is {name.Length} characters — at most {NAME_MAX_CHARS}";

        var separatorAt = name.IndexOf(SEPARATOR, StringComparison.Ordinal);

        if (separatorAt < 0 || name.IndexOf(SEPARATOR, separatorAt + SEPARATOR.Length, StringComparison.Ordinal) >= 0)
            return $"the name must contain exactly one '{SEPARATOR}' between the platform code and the words, e.g. 'AI-Orch · limits rework'";

        var code = name[..separatorAt];

        if (code.Length == 0 || code.Length > CODE_MAX_CHARS || code.Contains(' '))
            return $"the platform code before '{SEPARATOR}' must be 1-{CODE_MAX_CHARS} characters with no spaces, e.g. 'AI-Orch' or 'IS'";

        var words = name[(separatorAt + SEPARATOR.Length)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length < MIN_WORDS || words.Length > MAX_WORDS)
            return $"the name has {words.Length} word(s) after the code — it needs {MIN_WORDS}-{MAX_WORDS}, 3 is best";

        return null;
    }
}
