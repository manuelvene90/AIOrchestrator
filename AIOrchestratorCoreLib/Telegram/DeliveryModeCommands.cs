using System.Diagnostics.CodeAnalysis;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// THE DELIVERY-MODE TOGGLES, as the words the owner sends — /dnd, /mute, /unmute and the app-wide pair
/// in both spellings (Telegram's menu only allows the underscore; a hand-typed hyphen works as well).
///
/// <para>
/// ONE LIST, TWO READERS (plan 03 Task 5b). The inbound loop's typed-command chain defers these until
/// the batch is done — a toggle must not race the batch's ✓ acks, and a /dnd must not be auto-unmuted by
/// the very message that requested it — and the same loop's "the owner tapped something, lift DND" rule
/// must skip a TAPPED one for the second of those reasons. The list was inline in the chain as a
/// seven-way <c>||</c>; a second copy of it beside the lift would be two places to add the next mode
/// command and one of them forgotten (CLAUDE.md decision 12).
/// </para>
/// </summary>
public static class DeliveryModeCommands
{
    static readonly HashSet<string> MODE_COMMANDS = new(
        ["dnd", "mute", "unmute", "dnd-all", "mute-all", "dnd_all", "mute_all"],
        StringComparer.Ordinal);

    /// <summary>
    /// Whether the lexed command is one of the delivery-mode toggles. Ordinal, like the lexer's lowercased
    /// output. NotNullWhen so the chain that defers the toggle keeps knowing the command is not null.
    /// </summary>
    public static bool Is_ModeCommand([NotNullWhen(true)] string? command)
    {
        return command != null && MODE_COMMANDS.Contains(command);
    }
}
