using System.Net;

namespace AIOrchestratorCoreLib.Web.SettingsWebHost;

/// <summary>
/// WHAT THE LISTENER DECIDES, AS PURE FUNCTIONS (plan 04 Task 7) — kept out of <see cref="SettingsWebHostModel"/>
/// because no smoke test can reach them: every caller of a loopback socket is loopback, and a non-loopback
/// interface is the one address a test must never bind. <c>SettingsWebHostPolicyTests</c> pins them without a
/// socket. Everything about a REQUEST'S MEANING stays in <see cref="SettingsRequest_Handler"/>; these are only
/// about the socket: who may call, what to bind, how much to read.
/// </summary>
public static class SettingsWebHost_Policy
{
    /// <summary>
    /// THE LARGEST BODY THE LISTENER READS (Task 7 carry). The biggest honest body is one row's value — a list of
    /// a few dozen words — so 64 KiB is two orders of magnitude of room, and anything larger is answered 413
    /// before it is buffered: a client that streams gigabytes at an open loopback port does not get to make the
    /// bridge's process allocate them.
    /// </summary>
    public const int MAX_BODY_BYTES = 64 * 1024;

    const string LOCALHOST = "localhost";
    const string LOOPBACK_V4 = "127.0.0.1";

    /// <summary>
    /// WHETHER THE CALLER IS ON THIS MACHINE, by its socket address (Task 7 carry). The handler's Host allowlist
    /// is the DNS-rebinding defence — a browser tricked into calling loopback still names the attacker's host —
    /// and says nothing about where a request CAME from: a client on another machine types "Host: localhost" by
    /// hand. An IPv4-mapped IPv6 address (<c>::ffff:127.0.0.1</c>, what a dual-stack socket reports) is read as
    /// the IPv4 address it carries, which <see cref="IPAddress.IsLoopback"/> alone would call not-loopback.
    /// <para>
    /// ON WINDOWS THIS IS THE ONLY THING BETWEEN THE LAN AND AN OPEN EDIT (measured 2026-09-23): http.sys listens on
    /// 0.0.0.0 for a registered port and records the <c>localhost</c> prefix as a host-NAME registration, so a
    /// request to this machine's LAN address carrying <c>Host: localhost:&lt;port&gt;</c> reaches the listener and
    /// passes the handler's Host allowlist. It is answered 403 here —
    /// <c>SettingsWebHostSmokeTests.ACallerOnAnotherInterface_NamingLocalhost_IsNotServed</c> pins it end to end and
    /// goes red without this check. (The managed listener on Linux and macOS binds the loopback address itself, so
    /// there the connection is refused before any of this runs.)
    /// </para>
    /// </summary>
    public static bool Is_LoopbackCaller(IPAddress? address)
    {
        if (address == null)
            return false;

        return IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
    }

    /// <summary>
    /// Whether a <c>web.listen</c> host names this machine's loopback: <c>localhost</c> (any case) or a loopback
    /// literal (<c>127.0.0.0/8</c>, <c>::1</c>). <c>0.0.0.0</c> and <c>::</c> are EVERY interface, not loopback.
    /// </summary>
    public static bool Is_LoopbackListenHost(string host)
    {
        if (string.Equals(host, LOCALHOST, StringComparison.OrdinalIgnoreCase))
            return true;

        return IPAddress.TryParse(host, out var address) && Is_LoopbackCaller(address);
    }

    /// <summary>
    /// THE PREFIXES TO REGISTER — literal hosts only, never <c>+</c> or <c>*</c> (Task 7 carry). For the loopback
    /// default this is <c>127.0.0.1</c> and, when <paramref name="includeLocalhost"/> (see
    /// <see cref="Refuse_Localhost_OrNull"/>), <c>localhost</c> — the two names the handler accepts
    /// (<see cref="SettingsRequest_Handler.LOOPBACK_HOST_NAMES"/>). The managed HttpListener (Linux, macOS — where the
    /// headless daemon runs) routes a request on the NAME in its Host header, so without a <c>localhost</c> prefix a
    /// browser that opened <c>localhost:&lt;port&gt;</c> through an SSH tunnel (<c>ssh -L 8080:127.0.0.1:7391</c>) is
    /// refused by the listener before the handler sees it. <c>127.0.0.1</c> always comes first and is always there:
    /// it is the one name that is never ambiguous, and the model's fallback keeps it when the pair fails to bind.
    /// <c>[::1]</c>, another <c>127.x</c> address, or an interface is registered exactly as written.
    /// </summary>
    public static IReadOnlyList<string> Build_Prefixes(string host, int port, bool includeLocalhost)
    {
        if (!Wants_Localhost(host))
            return [Build_Prefix(host, port)];

        return includeLocalhost
            ? [Build_Prefix(LOOPBACK_V4, port), Build_Prefix(LOCALHOST, port)]
            : [Build_Prefix(LOOPBACK_V4, port)];
    }

    /// <summary>Whether <paramref name="host"/> is the loopback default this class widens to the 127.0.0.1 + localhost pair.</summary>
    public static bool Wants_Localhost(string host)
    {
        return string.Equals(host, LOOPBACK_V4, StringComparison.Ordinal) || string.Equals(host, LOCALHOST, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// WHY <c>localhost</c> MUST NOT BE REGISTERED ON THIS MACHINE, or null to register it (fix round 1, ruling P38).
    /// <list type="bullet">
    /// <item><b>Windows</b> (http.sys): always registered. http.sys binds no socket per prefix; it matches the
    /// <c>localhost</c> name on the port it already holds, and the caller check (<see cref="Is_LoopbackCaller"/>) is
    /// the pinned defence against the LAN callers that makes reachable.</item>
    /// <item><b>The managed listener</b> (Linux, macOS): it binds each prefix to
    /// <c>Dns.GetHostAddresses(host)[0]</c>. Where <c>::1 localhost</c> comes first — Debian and Ubuntu ship it that
    /// way — the <c>localhost</c> prefix binds <c>[::1]:port</c>, and a tunnel delivering to 127.0.0.1 with
    /// <c>Host: localhost:8080</c> gets the listener's own 400/404. So it is registered only where
    /// <paramref name="resolved"/> puts an IPv4 loopback address FIRST; otherwise the reason is returned, and the
    /// model logs it as the instruction a tunnel user needs.</item>
    /// </list>
    /// </summary>
    public static string? Refuse_Localhost_OrNull(bool isWindows, IReadOnlyList<IPAddress>? resolved)
    {
        if (isWindows)
            return null;

        var first = resolved is { Count: > 0 } ? resolved[0] : null;

        if (first != null && first.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && IPAddress.IsLoopback(first))
            return null;

        var named = first == null ? "does not resolve" : $"resolves to {first} first";

        return $"'{LOCALHOST}' is not registered — on this machine it {named}, which this listener would bind instead of "
            + $"{LOOPBACK_V4}. Through an SSH tunnel, open http://{LOOPBACK_V4}:<local port>/ (not localhost).";
    }

    /// <summary>What this machine's resolver answers for <c>localhost</c>, in its order, or null when it cannot answer.</summary>
    public static IReadOnlyList<IPAddress>? Resolve_Localhost_OrNull()
    {
        try
        {
            return Dns.GetHostAddresses(LOCALHOST);
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// WHY A <c>web.listen</c> HOST MUST NOT BE BOUND, or null to bind it (Task 7 carry). While <c>web.token</c>
    /// is empty the handler lets any caller edit (D4) — an accepted cost on loopback, where only this machine's
    /// accounts can reach the port, and handing the machine to the network on an interface. So a non-loopback
    /// address is refused until a token exists. With one set it is the owner's explicit choice and is tried;
    /// the caller check above still refuses every caller not on this machine, so an interface serves nothing
    /// but refusals — the page is reached from elsewhere through a tunnel to loopback (spec §8.3's limit).
    /// </summary>
    public static string? Refuse_Bind_OrNull(string host, bool tokenIsSet)
    {
        if (tokenIsSet || Is_LoopbackListenHost(host))
            return null;

        return $"'{host}' is not a loopback address, and web.token is empty — editing would be open to the network. "
            + "Set web.token first (config.json), or keep web.listen on 127.0.0.1 and reach the page through an SSH tunnel.";
    }

    static string Build_Prefix(string host, int port)
    {
        var literal = IPAddress.TryParse(host, out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{host}]"
            : host;

        return $"http://{literal}:{port}/";
    }
}
