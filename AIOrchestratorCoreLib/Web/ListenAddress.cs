using System.Globalization;

namespace AIOrchestratorCoreLib.Web;

/// <summary>
/// `host:port` for `web.listen` (an IPv6 literal takes brackets, e.g. "[::1]:7391" — decided and
/// pinned here, because an unbracketed IPv6 address makes the last ':' ambiguous between an address
/// colon and the port's), or the literal <see cref="OFF"/> for no listener at all. Plan 04's HTTP
/// listener (Task 7) is the consumer of <see cref="Parse_OrNull"/> and <see cref="Is_Off"/> only —
/// <see cref="Problem_OrNull"/> exists so <c>SettingValidators.Validate_OrNull</c> can phrase an
/// owner-facing refusal without a second copy of the split logic (decision 21: the catalogue's
/// validator is the one place a message is invented, never the listener or a renderer).
/// </summary>
public static class ListenAddress
{
    public const string OFF = "off";

    /// <summary>
    /// The bound host and port, or null for "off", for anything that does not parse as `host:port`,
    /// and for a port outside 1-65535. Never throws — this reads config text an owner may have
    /// hand-edited, and a parser of untrusted input swallows rather than throws.
    /// </summary>
    public static (string Host, int Port)? Parse_OrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Is_Off(value))
            return null;

        var (host, portText) = Split(value);

        if (host == null || portText.Length == 0)
            return null;

        if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
            return null;

        return port is >= 1 and <= 65535 ? (host, port) : null;
    }

    public static bool Is_Off(string? value) => string.Equals(value?.Trim(), OFF, StringComparison.Ordinal);

    /// <summary>
    /// What is wrong with <paramref name="value"/> as a `web.listen` value, or null when it is "off"
    /// or a valid `host:port`. Not part of the surface Task 7 consumes — this is diagnostic detail for
    /// the catalogue's validator to turn into a refusal, which is deliberately NOT reachable from
    /// <see cref="Parse_OrNull"/>'s plain nullable return: the listener only ever needs to know whether
    /// to bind, never why not.
    /// </summary>
    internal static string? Problem_OrNull(string value)
    {
        if (Is_Off(value))
            return null;

        var (host, portText) = Split(value);

        if (host == null)
            return $"'{value}' has no ':port' — expected 'host:port', an IPv6 literal in brackets like '[::1]:port', or the word '{OFF}'";

        if (portText.Length == 0)
            return $"'{value}' has a ':' but no port after it";

        if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
            return $"'{portText}' in '{value}' is not a port number";

        if (port is < 1 or > 65535)
            return $"port {port} in '{value}' is outside 1-65535";

        return null;
    }

    /// <summary>
    /// Splits on the bracket close for an IPv6 literal (`[host]:port`), otherwise on the LAST ':' — a
    /// bare hostname or IPv4 address carries no colon of its own, so the last one is always the port's.
    /// A null host means "not splittable at all"; an empty port text means "a ':' with nothing after
    /// it" — both are reported by <see cref="Problem_OrNull"/>, both are refused by
    /// <see cref="Parse_OrNull"/>.
    /// </summary>
    static (string? Host, string PortText) Split(string value)
    {
        var text = value.Trim();

        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');

            if (close < 0 || close + 1 >= text.Length || text[close + 1] != ':')
                return (null, "");

            var host = text[1..close];
            return host.Length == 0 ? (null, "") : (host, text[(close + 2)..]);
        }

        var colon = text.LastIndexOf(':');

        return colon <= 0 ? (null, "") : (text[..colon], text[(colon + 1)..]);
    }
}
