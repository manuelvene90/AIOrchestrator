using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Limits.ClaudeAccount;

/// <summary>
/// The account id out of the CLI's global config (<c>~/.claude.json</c>): <c>oauthAccount.accountUuid</c>.
/// Measured on this machine 2026-09-23 — the block also carries the e-mail address, which is
/// deliberately NOT read: the uuid is the identity, and an address is personal data the app has no
/// use for.
/// </summary>
public static class ClaudeAccount_Parser
{
    public static string? Read_AccountId_OrNull(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var accountId = JsonNode.Parse(json)?["oauthAccount"]?["accountUuid"];

            if (accountId is not JsonValue value || !value.TryGetValue<string>(out var text))
                return null;

            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch
        {
            // A file the CLI is halfway through rewriting is not a different account.
            return null;
        }
    }
}
