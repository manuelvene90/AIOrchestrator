using System.Net;
using AIOrchestratorCoreLib.Web.SettingsWebHost;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Web;

/// <summary>
/// THE LISTENER'S DECISIONS, WITH NO SOCKET — the half of Task 7 that a smoke test cannot reach. Every caller of
/// a loopback socket IS loopback, so the remote-address guard would be green in every smoke whether it existed
/// or not; and the one address a test may never bind (a non-loopback interface) is exactly the case the bind
/// refusal is for. Both are pinned here, as pure functions.
/// </summary>
public class SettingsWebHostPolicyTests
{
    /// <summary>
    /// THE CALLER'S ADDRESS, not its Host header (Task 7 carry): the handler's Host allowlist defends DNS
    /// rebinding — a browser tricked into calling loopback — and says nothing about WHERE a request came from.
    /// A client on another machine can send "Host: localhost" by hand.
    /// </summary>
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.8.9.10", true)]
    [InlineData("::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("10.0.0.5", false)]
    [InlineData("192.0.2.1", false)]
    [InlineData("::ffff:192.0.2.1", false)]
    [InlineData("fe80::1", false)]
    public void Is_LoopbackCaller_ReadsTheAddress_IPv4MappedIncluded(string address, bool expected)
    {
        Assert.Equal(expected, SettingsWebHost_Policy.Is_LoopbackCaller(IPAddress.Parse(address)));
    }

    [Fact]
    public void Is_LoopbackCaller_WithNoAddress_IsFalse()
    {
        Assert.False(SettingsWebHost_Policy.Is_LoopbackCaller(null));
    }

    /// <summary>
    /// LITERAL HOSTS ONLY, NEVER '+' OR '*' — and the loopback default registers BOTH names the handler accepts,
    /// because HttpListener routes on the Host header's NAME: with 127.0.0.1 alone, a browser that opened
    /// "localhost:&lt;port&gt;" (the ordinary way through an SSH tunnel) would be refused by the listener before
    /// the handler ever saw it. 127.0.0.1 first, so the fallback that drops a failing second prefix keeps it.
    /// </summary>
    [Theory]
    [InlineData("127.0.0.1", new[] { "http://127.0.0.1:7391/", "http://localhost:7391/" })]
    [InlineData("localhost", new[] { "http://127.0.0.1:7391/", "http://localhost:7391/" })]
    [InlineData("LocalHost", new[] { "http://127.0.0.1:7391/", "http://localhost:7391/" })]
    [InlineData("::1", new[] { "http://[::1]:7391/" })]
    [InlineData("127.0.0.2", new[] { "http://127.0.0.2:7391/" })]
    [InlineData("192.0.2.1", new[] { "http://192.0.2.1:7391/" })]
    public void Build_Prefixes_IsLiteral_AndCoversBothLoopbackNames(string host, string[] expected)
    {
        var prefixes = SettingsWebHost_Policy.Build_Prefixes(host, 7391);

        Assert.Equal(expected, prefixes);
        Assert.DoesNotContain(prefixes, prefix => prefix.Contains('+') || prefix.Contains('*'));
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("localhost", true)]
    [InlineData("::1", true)]
    [InlineData("127.1.2.3", true)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::", false)]
    [InlineData("192.0.2.1", false)]
    [InlineData("my-vps.example", false)]
    public void Is_LoopbackListenHost_NamesOnlyThisMachinesLoopback(string host, bool expected)
    {
        Assert.Equal(expected, SettingsWebHost_Policy.Is_LoopbackListenHost(host));
    }

    /// <summary>
    /// A NON-LOOPBACK web.listen IS NEVER BOUND WHILE web.token IS EMPTY (Task 7 carry). D4 accepts open editing
    /// on loopback; on an interface it would be handing the machine to the network. With a token set it is the
    /// owner's explicit choice and is tried (on Windows it then needs a URL ACL, which this app never adds).
    /// </summary>
    [Fact]
    public void Refuse_Bind_OrNull_RefusesAnInterface_OnlyWhileTheTokenIsEmpty()
    {
        Assert.NotNull(SettingsWebHost_Policy.Refuse_Bind_OrNull("192.0.2.1", tokenIsSet: false));
        Assert.NotNull(SettingsWebHost_Policy.Refuse_Bind_OrNull("0.0.0.0", tokenIsSet: false));
        Assert.Null(SettingsWebHost_Policy.Refuse_Bind_OrNull("192.0.2.1", tokenIsSet: true));
        Assert.Null(SettingsWebHost_Policy.Refuse_Bind_OrNull("127.0.0.1", tokenIsSet: false));
        Assert.Null(SettingsWebHost_Policy.Refuse_Bind_OrNull("localhost", tokenIsSet: false));
    }
}
