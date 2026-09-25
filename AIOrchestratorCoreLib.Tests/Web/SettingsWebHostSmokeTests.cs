using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Logging.OrchestrationLogEntry;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.TestSupport;
using AIOrchestratorCoreLib.Web;
using AIOrchestratorCoreLib.Web.SettingsWebHost;
using Xunit;
using Xunit.Abstractions;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Web;

/// <summary>
/// ONE REAL SOCKET, AND IT REFUSES TO RUN RATHER THAN CERTIFY NOTHING. HttpListener cannot bind port 0,
/// so this test probes for a free loopback port; if it cannot get one, or cannot bind, it SKIPS with a
/// message naming why. CLAUDE.md decision 20: a harness that cannot find what it tests must fail loudly
/// rather than certify the absence of the thing it never ran — a green "web host works" from a test that
/// never bound a socket is exactly the sixteen confident failures that rule was written for.
///
/// <para>
/// THE SKIP IS DECIDED BY THE ATTRIBUTE, NEVER BY AN EARLY RETURN (ruling P22): <see cref="RequiresLoopbackListenerFactAttribute"/>
/// probes in its constructor and sets <c>Skip</c> with the reason, so a machine that cannot bind shows these
/// as SKIPPED, by name, with why — a <c>[Fact]</c> that returned early would show them as passed.
/// </para>
/// <para>
/// EVERY HOST HERE RUNS ON A TEMP SUPERVISION ROOT (ruling P9) and binds only <c>127.0.0.1</c> /
/// <c>localhost</c>: nothing here ever binds a non-loopback address — the one non-loopback case is the host
/// REFUSING to, which is what it pins.
/// </para>
/// </summary>
public class SettingsWebHostSmokeTests : IDisposable
{
    static readonly TimeSpan RUN_BUDGET = TimeSpan.FromSeconds(20);

    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;
    readonly RecordingLog _log = new();
    readonly CancellationTokenSource _stop = new();
    readonly ITestOutputHelper _output;
    Task? _running;

    public SettingsWebHostSmokeTests(ITestOutputHelper output)
    {
        _output = output;
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-web-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        _stop.Cancel();

        try
        {
            _running?.Wait(RUN_BUDGET);
        }
        catch (AggregateException)
        {
            // A faulted run has already failed its own test; the temp tree still goes.
        }

        _stop.Dispose();
        TempTree.Delete_BestEffort(_tempRoot);
    }

    // ---------------------------------------------------------------------------------------
    // Serving
    // ---------------------------------------------------------------------------------------

    [RequiresLoopbackListenerFact]
    public async Task TheHost_ServesTheSettingsJson_OverARealLoopbackSocket()
    {
        var (host, baseUrl) = await Start_Async(listen: $"127.0.0.1:{LoopbackListener.Find_FreePort()}");

        using var client = new HttpClient();
        using var response = await client.GetAsync(baseUrl + SettingsRequest_Handler.SETTINGS_PATH.TrimStart('/'));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(JsonNode.Parse(body)!["sections"]!.AsArray());
        Assert.True(host.IsListening);
        Assert.Equal(baseUrl, host.ListeningOn_OrNull);
        _output.WriteLine($"RAN on {Environment.OSVersion}: bound {host.ListeningOn_OrNull}");
    }

    [RequiresLoopbackListenerFact]
    public async Task TheHost_ServesThePage_AtTheRoot()
    {
        var (_, baseUrl) = await Start_Async(listen: $"127.0.0.1:{LoopbackListener.Find_FreePort()}");

        using var client = new HttpClient();
        using var response = await client.GetAsync(baseUrl);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(SettingsPage_Reader.Read_Html(), body);

        // The page is never framed by another site: the clickjacking half of "open on loopback" (D4).
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [RequiresLoopbackListenerFact]
    public async Task APut_OverTheSocket_ReachesTheWriter_AndTheFileChanges()
    {
        var (_, baseUrl) = await Start_Async(listen: $"127.0.0.1:{LoopbackListener.Find_FreePort()}");

        using var client = new HttpClient();
        using var response = await client.PutAsync(
            baseUrl + SettingsRequest_Handler.SETTINGS_PATH.TrimStart('/'),
            new StringContent($"{{\"{INTERVAL_PATH}\": 45}}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var reading = SettingsSnapshot_Reader.Read_One_FromDisk_OrNull(INTERVAL_PATH, _paths, session: null, log: null)!;
        Assert.Equal(45, reading.Value_OrNull!.GetValue<long>());
        Assert.Contains("45", File.ReadAllText(_paths.ConfigFile), StringComparison.Ordinal);
    }

    /// <summary>
    /// web.token is re-read when the config provider's instance changes, not only at start: web.listen is
    /// Restart = Host, but an owner who sets a token by hand in config.json must not have to restart the bridge
    /// for the page to start demanding it (and a page that went on accepting token-less edits after the owner
    /// set one would be the worst way to be wrong).
    /// </summary>
    [RequiresLoopbackListenerFact]
    public async Task ATokenSetWhileRunning_IsEnforced_WithoutARestart()
    {
        var (_, baseUrl) = await Start_Async(listen: $"127.0.0.1:{LoopbackListener.Find_FreePort()}");
        using var client = new HttpClient();

        using (var open = await Put_Async(client, baseUrl, $"{{\"{INTERVAL_PATH}\": 40}}"))
            Assert.Equal(HttpStatusCode.OK, open.StatusCode);

        Write_Config(File.ReadAllText(_paths.ConfigFile), token: "s3cret-by-hand");

        using (var refused = await Put_Async(client, baseUrl, $"{{\"{INTERVAL_PATH}\": 41}}"))
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        using (var accepted = await Put_Async(client, baseUrl, $"{{\"{INTERVAL_PATH}\": 42}}", token: "s3cret-by-hand"))
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        // …which is why the row's restart label says it applies at once (ruling P37): this test is the behaviour that
        // makes that label true, so it pins the label too.
        Assert.Equal(RestartKinds.None, Catalog.Find_OrNull(SettingsSnapshot_Reader.MASKED_SECRET_PATH)!.Restart);
    }

    /// <summary>
    /// A TOKEN THAT CANNOT BE READ FAILS CLOSED (fix round 1, 2026-09-23). The first version read config.json through
    /// the tolerant read, which turns a garbage, empty (an editor truncating before it writes) or held file into
    /// <c>{}</c> — token <c>""</c>, and token-less edits open on a machine whose owner had set one. Now every edit is
    /// refused while the file does not read, with the right token too (nobody can know a token the host cannot
    /// read), and the real token works again the moment it does.
    /// </summary>
    [RequiresLoopbackListenerFact]
    public async Task AConfigJsonThatDoesNotParse_WhileATokenWasSet_RefusesEveryEdit_UntilItReadsAgain()
    {
        var (_, baseUrl) = await Start_Async(listen: $"127.0.0.1:{LoopbackListener.Find_FreePort()}");
        using var client = new HttpClient();
        var readable = File.ReadAllText(_paths.ConfigFile);
        Write_Config(readable, token: "s3cret");
        readable = File.ReadAllText(_paths.ConfigFile);

        using (var accepted = await Put_Async(client, baseUrl, $"{{\"{INTERVAL_PATH}\": 40}}", token: "s3cret"))
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        readable = File.ReadAllText(_paths.ConfigFile);

        foreach (var broken in new[] { "{ \"web\": { \"token\": ", "" })
        {
            Write_Raw(broken);

            using (var tokenLess = await Put_Async(client, baseUrl, $"{{\"{INTERVAL_PATH}\": 41}}"))
                Assert.Equal(HttpStatusCode.Unauthorized, tokenLess.StatusCode);

            using (var withToken = await Put_Async(client, baseUrl, $"{{\"{INTERVAL_PATH}\": 41}}", token: "s3cret"))
                Assert.Equal(HttpStatusCode.Unauthorized, withToken.StatusCode);
        }

        Write_Raw(readable);

        using (var again = await Put_Async(client, baseUrl, $"{{\"{INTERVAL_PATH}\": 42}}", token: "s3cret"))
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        Assert.Single(_log.Warnings, line => line.Contains("web.token could not be read", StringComparison.Ordinal));
    }

    /// <summary>
    /// The same, for a config.json another process holds open exclusively just after it changed — an editor saving,
    /// an antivirus scan. The failed read is NOT cached: the provider hands out ONE new instance, the host's read under
    /// that instance fails, and the first request after the holder lets go — same instance, nothing told the host the
    /// file changed — reads the real token. Caching the failure would keep every edit refused until the next write
    /// of config.json.
    /// </summary>
    [RequiresExclusiveOpenEnforcementFact]
    public async Task AConfigJsonHeldOpenExclusively_RefusesEveryEdit_AndTheRealTokenWorksOnceReleased()
    {
        var provider = new SteppedConfigProvider(_paths);
        var (_, baseUrl) = await Start_Async(listen: $"127.0.0.1:{LoopbackListener.Find_FreePort()}", token: "s3cret", provider: provider);
        using var client = new HttpClient();

        provider.Step();

        using (var holder = new FileStream(_paths.ConfigFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            using var tokenLess = await Put_Async(client, baseUrl, $"{{\"{INTERVAL_PATH}\": 41}}");
            Assert.Equal(HttpStatusCode.Unauthorized, tokenLess.StatusCode);

            using var withToken = await Put_Async(client, baseUrl, $"{{\"{INTERVAL_PATH}\": 41}}", token: "s3cret");
            Assert.Equal(HttpStatusCode.Unauthorized, withToken.StatusCode);
        }

        using var released = await Put_Async(client, baseUrl, $"{{\"{INTERVAL_PATH}\": 42}}", token: "s3cret");
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
    }

    /// <summary>
    /// THROUGH AN SSH TUNNEL THE BROWSER SAYS "localhost" AND ITS OWN PORT. <c>ssh -L 8080:127.0.0.1:7391</c> delivers
    /// the connection to 127.0.0.1:7391 carrying <c>Host: localhost:8080</c>, and HttpListener routes on that name:
    /// with the 127.0.0.1 prefix alone the listener would refuse it before the handler saw it. This is what the
    /// second, <c>localhost</c> prefix is for (<see cref="SettingsWebHost_Policy.Build_Prefixes"/>), and the
    /// handler deliberately ignores the port (<see cref="SettingsRequest_Handler.LOOPBACK_HOST_NAMES"/>).
    /// <para>
    /// MEASURED 2026-09-23 ON WINDOWS: http.sys matches an IP-literal prefix by ADDRESS and delivered even
    /// <c>Host: rebound.example</c> to the 127.0.0.1 prefix, so on Windows this test is green with or without the
    /// <c>localhost</c> prefix (mutation-checked: removing it stays green). It pins the prefix on the managed listener
    /// (Linux, macOS — the headless daemon this page exists for), which matches the Host NAME against its prefixes.
    /// </para>
    /// </summary>
    [RequiresLocalhostPrefixFact]
    public async Task ARequestThroughATunnel_NamedLocalhostOnAnotherPort_IsServed()
    {
        await Assert_TunnelServes_Async(tunnelHost: "localhost:8080");
    }

    /// <summary>
    /// The tunnel that works EVERYWHERE, the localhost prefix or not: the browser opened <c>127.0.0.1:&lt;local port&gt;</c>.
    /// This is the address the host's log tells a tunnel user to open on a machine where localhost is not registered
    /// (<see cref="SettingsWebHost_Policy.Refuse_Localhost_OrNull"/>), so it must never depend on that prefix.
    /// </summary>
    [RequiresLoopbackListenerFact]
    public async Task ARequestThroughATunnel_Named127001OnAnotherPort_IsServed()
    {
        await Assert_TunnelServes_Async(tunnelHost: "127.0.0.1:8080");
    }

    async Task Assert_TunnelServes_Async(string tunnelHost)
    {
        var (_, baseUrl) = await Start_Async(listen: $"127.0.0.1:{LoopbackListener.Find_FreePort()}");
        using var client = new HttpClient();

        foreach (var target in new[] { baseUrl, baseUrl + SettingsRequest_Handler.SETTINGS_PATH.TrimStart('/') })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            request.Headers.Host = tunnelHost;

            using var response = await client.SendAsync(request);

            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{target} with Host {tunnelHost} answered {(int)response.StatusCode}");
        }
    }

    /// <summary>
    /// THE HOST CHECK COVERS THE PAGE TOO (Task 7 carry). A DNS-rebinding site is same-origin to the owner's
    /// browser once its name resolves to 127.0.0.1, so it could fetch the page and read it; what it cannot
    /// change is the Host header. Both hosts' browsers send a loopback name, so this refuses nothing real.
    /// </summary>
    [RequiresLoopbackListenerFact]
    public async Task ARequestAddressedToAnotherName_IsRefused_ThePageIncluded()
    {
        var (_, baseUrl) = await Start_Async(listen: $"127.0.0.1:{LoopbackListener.Find_FreePort()}");
        using var client = new HttpClient();

        foreach (var target in new[] { baseUrl, baseUrl + SettingsRequest_Handler.SETTINGS_PATH.TrimStart('/') })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            request.Headers.Host = "rebound.example";

            using var response = await client.SendAsync(request);
            _output.WriteLine($"{target} with Host rebound.example: {(int)response.StatusCode}");

            // 421 from the handler; a listener that routes on the Host NAME (the managed one, on Linux and macOS)
            // may refuse first with its own 400/404. STATED, per decision 20's "never assert on a state with two
            // routes to it": there this test pins the PROPERTY — no page, no settings — not which layer refused.
            // On Windows it pins the route as well: http.sys matches the 127.0.0.1 prefix by address, both answers
            // measured 421 (2026-09-23), and removing the page's Host check turns this test red (mutation-checked).
            Assert.True(
                response.StatusCode is HttpStatusCode.MisdirectedRequest or HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
                $"{target} answered {(int)response.StatusCode}");
            Assert.DoesNotContain("<!doctype html>", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// A CALLER ON ANOTHER INTERFACE IS REFUSED, EVEN WHEN IT NAMES "localhost" (Task 7 carry). MEASURED 2026-09-23 ON
    /// WINDOWS: http.sys listens on 0.0.0.0 for a registered port and treats the <c>localhost</c> prefix as a HOST-NAME
    /// registration, so a request to this machine's LAN address carrying <c>Host: localhost:&lt;port&gt;</c> is routed
    /// to the listener, passes the handler's Host allowlist, and — with no token set — could edit. The caller check
    /// (<see cref="SettingsWebHost_Policy.Is_LoopbackCaller"/>) is what answers it 403. On Linux and macOS nothing is
    /// bound on that address and the connection itself is refused; both outcomes are "not served", which is what is
    /// asserted. The test CONNECTS to a non-loopback address and never binds one; it skips on a machine with none.
    /// </summary>
    [RequiresLoopbackListenerAndLanAddressFact]
    public async Task ACallerOnAnotherInterface_NamingLocalhost_IsNotServed()
    {
        var port = LoopbackListener.Find_FreePort();
        _ = await Start_Async(listen: $"127.0.0.1:{port}");
        var lan = LoopbackListener.Find_LanAddress_OrNull()!;

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(HttpMethod.Put, $"http://{lan}:{port}{SettingsRequest_Handler.SETTINGS_PATH}")
        {
            Content = new StringContent($"{{\"{INTERVAL_PATH}\": 45}}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Host = $"localhost:{port}";

        HttpResponseMessage? response = null;

        try
        {
            response = await client.SendAsync(request);
        }
        catch (HttpRequestException ex)
        {
            _output.WriteLine($"{lan}:{port} refused the connection ({ex.Message}) — nothing is bound there, as on the managed listener.");
        }

        using (response)
        {
            if (response != null)
            {
                _output.WriteLine($"{lan}:{port} with Host localhost answered {(int)response.StatusCode}");

                // On Windows http.sys DOES route this request to the listener (measured 2026-09-23), so the caller
                // check is the one answer — pinned exactly, per decision 20, not as one of three statuses. Elsewhere
                // a connection that got through at all could only have met the listener's own refusal.
                if (OperatingSystem.IsWindows())
                    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                else
                    Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
                        $"A caller on {lan} was answered {(int)response.StatusCode}");
            }
        }

        var interval = SettingsSnapshot_Reader.Read_One_FromDisk_OrNull(INTERVAL_PATH, _paths, session: null, log: null)!;
        Assert.NotEqual("45", interval.Value_OrNull?.ToJsonString());
    }

    /// <summary>
    /// NO CORS HEADER, EVER — the handler's OPTIONS refusal IS the CSRF defence while editing is open (D4): a
    /// cross-origin PUT is preflighted, and a preflight answered without Access-Control-Allow-* stops it.
    /// </summary>
    [RequiresLoopbackListenerFact]
    public async Task APreflight_IsRefused_AndNoAnswerCarriesACorsHeader()
    {
        var (_, baseUrl) = await Start_Async(listen: $"127.0.0.1:{LoopbackListener.Find_FreePort()}");
        using var client = new HttpClient();

        using var preflight = new HttpRequestMessage(HttpMethod.Options, baseUrl + SettingsRequest_Handler.SETTINGS_PATH.TrimStart('/'));
        preflight.Headers.Add("Origin", "null");
        preflight.Headers.Add("Access-Control-Request-Method", "PUT");

        using var refused = await client.SendAsync(preflight);
        using var get = await client.GetAsync(baseUrl + SettingsRequest_Handler.SETTINGS_PATH.TrimStart('/'));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, refused.StatusCode);
        Assert.Contains("GET", refused.Content.Headers.Allow);

        foreach (var response in new[] { refused, get })
        {
            var names = response.Headers.Concat(response.Content.Headers).Select(header => header.Key);
            Assert.DoesNotContain(names, name => name.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase));
        }
    }

    [RequiresLoopbackListenerFact]
    public async Task AnOversizedBody_IsRefused_AndNothingIsWritten()
    {
        var (_, baseUrl) = await Start_Async(listen: $"127.0.0.1:{LoopbackListener.Find_FreePort()}");
        var before = File.ReadAllText(_paths.ConfigFile);
        var padding = new string(' ', SettingsWebHost_Policy.MAX_BODY_BYTES + 1);

        using var client = new HttpClient();
        using var response = await Put_Async(client, baseUrl, $"{{\"{INTERVAL_PATH}\": 45}}{padding}");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(before, File.ReadAllText(_paths.ConfigFile));
    }

    // ---------------------------------------------------------------------------------------
    // Not serving
    // ---------------------------------------------------------------------------------------

    /// <summary>web.listen = "off" means NO LISTENER, not a listener that refuses.</summary>
    [Fact]
    public async Task WithListenOff_NothingIsBound_AndTheHostSaysSo()
    {
        Write_Config("{}", listen: "off");
        var host = Create_Host();

        await host.Run_Async(_stop.Token).WaitAsync(RUN_BUDGET);

        Assert.False(host.IsListening);
        Assert.Null(host.ListeningOn_OrNull);
        Assert.Contains(_log.Infos, line => line.Contains("off", StringComparison.Ordinal));
        Assert.Empty(_log.Warnings);
    }

    /// <summary>
    /// A NON-LOOPBACK ADDRESS IS NEVER BOUND WHILE web.token IS EMPTY (Task 7 carry): D4's open editing is an
    /// accepted cost on loopback and would be handing the machine over on an interface. One warning, no socket.
    /// 192.0.2.1 is TEST-NET-1 (RFC 5737) — no machine owns it, so even a host that tried could not bind it.
    /// </summary>
    [Fact]
    public async Task ANonLoopbackListen_WithNoToken_IsNeverBound_AndSaysWhy()
    {
        Write_Config("{}", listen: "192.0.2.1:7391");
        var host = Create_Host();

        await host.Run_Async(_stop.Token).WaitAsync(RUN_BUDGET);

        Assert.False(host.IsListening);
        var warning = Assert.Single(_log.Warnings);
        Assert.Contains("192.0.2.1:7391", warning, StringComparison.Ordinal);
        Assert.Contains("token", warning, StringComparison.Ordinal);
    }

    /// <summary>
    /// A PORT ALREADY IN USE IS ONE WARNING LINE AND A HOST THAT KEEPS RUNNING, never a host that dies. The
    /// WPF app and the daemon can both hold a supervision root on one machine and they ship the same default
    /// port; the second one must not take the bridge down with it.
    /// </summary>
    [RequiresLoopbackListenerFact]
    public async Task APortAlreadyInUse_LogsOnce_AndDoesNotStopTheHost()
    {
        var port = LoopbackListener.Find_FreePort();
        using var squatter = new HttpListener();
        squatter.Prefixes.Add($"http://127.0.0.1:{port}/");
        squatter.Start();

        Write_Config("{}", listen: $"127.0.0.1:{port}");
        var host = Create_Host();

        // Completes — neither throws nor hangs — which is what lets the engine beside it keep running.
        await host.Run_Async(_stop.Token).WaitAsync(RUN_BUDGET);

        Assert.False(host.IsListening);
        var warning = Assert.Single(_log.Warnings);
        Assert.Contains($"127.0.0.1:{port}", warning, StringComparison.Ordinal);
        Assert.Empty(_log.Errors);
    }

    // ---------------------------------------------------------------------------------------
    // The browser check (ruling P9)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// NOT A TEST — A FIXTURE FOR A HUMAN WITH A BROWSER, off unless asked for. Ruling P9: the page's visual check
    /// never runs the WPF app or the daemon on the real supervision root (a second getUpdates poller, a watchdog
    /// spawning sessions); it runs against THIS: the real host, on a temp root, held open for
    /// <see cref="HOLD_MINUTES_ENV"/> minutes on <see cref="HOLD_PORT_ENV"/> (default 7391), so the checklist in
    /// the task report can be walked at <c>127.0.0.1:&lt;port&gt;/</c>. Skipped, by name and with the reason,
    /// whenever the variable is absent — which is every ordinary run.
    /// </summary>
    [HeldOpenForABrowserFact]
    public async Task HeldOpen_ForABrowserCheck_OnATempRoot()
    {
        var minutes = int.Parse(Environment.GetEnvironmentVariable(HOLD_MINUTES_ENV)!, System.Globalization.CultureInfo.InvariantCulture);
        var port = int.TryParse(Environment.GetEnvironmentVariable(HOLD_PORT_ENV), out var chosen) ? chosen : 7391;

        var (host, baseUrl) = await Start_Async(listen: $"127.0.0.1:{port}");
        _output.WriteLine($"Settings page held open for {minutes} min at {baseUrl} — temp root {_tempRoot}");

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(minutes), _stop.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.True(host.IsListening);
    }

    public const string HOLD_MINUTES_ENV = "AIORCH_WEB_SMOKE_HOLD_MINUTES";
    public const string HOLD_PORT_ENV = "AIORCH_WEB_SMOKE_PORT";

    // ---------------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------------

    const string INTERVAL_PATH = "phone.status.intervalMinutes";

    ISettingsWebHost Create_Host(IOrchestratorConfigProvider? provider = null)
    {
        return SettingsWebHost_Factory.Create(_paths, provider ?? OrchestratorConfigProvider_Factory.Create(_paths, _log), _log);
    }

    /// <summary>
    /// A provider whose instance changes ONLY when the test says so — the real one changes it only when config.json's
    /// stamp moves. It is how a test separates "the file became readable again" from "the file changed".
    /// </summary>
    sealed class SteppedConfigProvider(ISupervisionPaths paths) : IOrchestratorConfigProvider
    {
        IOrchestratorConfig _current = OrchestratorConfig_Loader.Load_OrEmpty(paths);

        public IOrchestratorConfig Get_Current() => _current;

        /// <summary>A NEW instance, as the real provider hands out after a write — loaded now, while the file reads.</summary>
        public void Step() => _current = OrchestratorConfig_Loader.Load_OrEmpty(paths);
    }

    /// <summary>Starts the host on a temp root and waits for it to report the bind, or fails naming what it logged.</summary>
    async Task<(ISettingsWebHost Host, string BaseUrl)> Start_Async(string listen, string? token = null, IOrchestratorConfigProvider? provider = null)
    {
        Write_Config("{}", listen: listen, token: token);
        var host = Create_Host(provider);
        _running = Task.Run(() => host.Run_Async(_stop.Token));

        var deadline = DateTime.UtcNow + RUN_BUDGET;

        while (!host.IsListening && !_running.IsCompleted && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.True(host.IsListening, "The host did not bind. Warnings: " + string.Join(" | ", _log.Warnings) + " Errors: " + string.Join(" | ", _log.Errors));

        return (host, host.ListeningOn_OrNull!);
    }

    void Write_Config(string configJson, string? listen = null, string? token = null)
    {
        var root = JsonNode.Parse(configJson)!.AsObject();
        var web = root["web"] as JsonObject ?? new JsonObject();

        if (listen != null)
            web["listen"] = listen;

        if (token != null)
            web["token"] = token;

        root["web"] = web.DeepClone();
        File.WriteAllText(_paths.ConfigFile, root.ToJsonString());

        // The provider notices a new instance by the file's write stamp; make sure this write has one of its own.
        File.SetLastWriteTimeUtc(_paths.ConfigFile, DateTime.UtcNow.AddSeconds(Interlocked.Increment(ref _stampBump)));
    }

    int _stampBump;

    /// <summary>Replaces config.json with exactly <paramref name="text"/>, stamped so the provider sees a new instance.</summary>
    void Write_Raw(string text)
    {
        File.WriteAllText(_paths.ConfigFile, text);
        File.SetLastWriteTimeUtc(_paths.ConfigFile, DateTime.UtcNow.AddSeconds(Interlocked.Increment(ref _stampBump)));
    }

    static Task<HttpResponseMessage> Put_Async(HttpClient client, string baseUrl, string body, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, baseUrl + SettingsRequest_Handler.SETTINGS_PATH.TrimStart('/'))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        if (token != null)
            request.Headers.Add(SettingsRequest_Handler.TOKEN_HEADER, token);

        return client.SendAsync(request);
    }

    sealed class RecordingLog : IOrchestrationLog
    {
        readonly Lock _lock = new();
        readonly List<string> _infos = [];
        readonly List<string> _warnings = [];
        readonly List<string> _errors = [];

        public IReadOnlyList<string> Infos { get { lock (_lock) return [.. _infos]; } }
        public IReadOnlyList<string> Warnings { get { lock (_lock) return [.. _warnings]; } }
        public IReadOnlyList<string> Errors { get { lock (_lock) return [.. _errors]; } }

        public void Log_Info(string orchId, string message) { lock (_lock) _infos.Add(message); }
        public void Log_Warning(string orchId, string message) { lock (_lock) _warnings.Add(message); }
        public void Log_Error(string orchId, string message, Exception? exception) { lock (_lock) _errors.Add(message); }

        public event Action<IOrchestrationLogEntry>? EntryLogged
        {
            add { }
            remove { }
        }
    }
}

/// <summary>A free loopback TCP port, and whether this machine lets an HttpListener bind one at all.</summary>
static class LoopbackListener
{
    /// <summary>
    /// Asks the OS for a free port by binding port 0 and letting it go. A race with another process taking it
    /// in between is possible and LOUD: the test then fails naming the bind, never passes without one.
    /// </summary>
    public static int Find_FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();

        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    /// <summary>This machine's first up, non-loopback, non-link-local IPv4 unicast address, or null when it has none.</summary>
    public static IPAddress? Find_LanAddress_OrNull()
    {
        return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(address)
                && !address.ToString().StartsWith("169.254.", StringComparison.Ordinal));
    }

    public static readonly Lazy<string?> UNBINDABLE_REASON = new(Probe_Unbindable_Reason_OrNull);

    static string? Probe_Unbindable_Reason_OrNull()
    {
        if (!HttpListener.IsSupported)
            return $"HttpListener is not supported on {Environment.OSVersion}, so there is no socket to smoke-test.";

        int port;

        try
        {
            port = Find_FreePort();
        }
        catch (Exception ex)
        {
            return $"No free loopback TCP port could be found ({ex.GetType().Name}: {ex.Message}).";
        }

        var prefix = $"http://127.0.0.1:{port}/";

        try
        {
            using var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();
            listener.Stop();

            return null;
        }
        catch (Exception ex)
        {
            return $"An HttpListener cannot bind {prefix} on this machine ({ex.GetType().Name}: {ex.Message}), so these smokes would test nothing.";
        }
    }
}

/// <summary>Runs only where an HttpListener can bind a loopback port; otherwise SKIPS with the reason (ruling P22).</summary>
public sealed class RequiresLoopbackListenerFactAttribute : FactAttribute
{
    public RequiresLoopbackListenerFactAttribute()
    {
        var reason = LoopbackListener.UNBINDABLE_REASON.Value;

        if (reason != null)
            Skip = reason;
    }
}

/// <summary>Opt-in only: runs when <see cref="SettingsWebHostSmokeTests.HOLD_MINUTES_ENV"/> names a whole number of minutes.</summary>
public sealed class HeldOpenForABrowserFactAttribute : FactAttribute
{
    public HeldOpenForABrowserFactAttribute()
    {
        var minutes = Environment.GetEnvironmentVariable(SettingsWebHostSmokeTests.HOLD_MINUTES_ENV);

        if (!int.TryParse(minutes, out var parsed) || parsed <= 0)
            Skip = $"A browser fixture, not a test: set {SettingsWebHostSmokeTests.HOLD_MINUTES_ENV}=<minutes> to hold the settings page open on a temp root.";
        else if (LoopbackListener.UNBINDABLE_REASON.Value is { } reason)
            Skip = reason;
    }
}

/// <summary>The loopback probe, plus a non-loopback local address to call FROM — never one to bind.</summary>
public sealed class RequiresLoopbackListenerAndLanAddressFactAttribute : FactAttribute
{
    public RequiresLoopbackListenerAndLanAddressFactAttribute()
    {
        if (LoopbackListener.UNBINDABLE_REASON.Value is { } reason)
            Skip = reason;
        else if (LoopbackListener.Find_LanAddress_OrNull() == null)
            Skip = "This machine has no up, non-loopback IPv4 address to call the listener from.";
    }
}

/// <summary>
/// Runs only where the host registers the <c>localhost</c> prefix; elsewhere SKIPS, by name, with the host's own
/// reason — the managed listener on a machine whose resolver lists <c>::1 localhost</c> first (ruling P38), where the
/// 127.0.0.1-named tunnel test is the one that covers the tunnel.
/// </summary>
public sealed class RequiresLocalhostPrefixFactAttribute : FactAttribute
{
    public RequiresLocalhostPrefixFactAttribute()
    {
        if (LoopbackListener.UNBINDABLE_REASON.Value is { } reason)
            Skip = reason;
        else if (SettingsWebHost_Policy.Refuse_Localhost_OrNull(OperatingSystem.IsWindows(), SettingsWebHost_Policy.Resolve_Localhost_OrNull()) is { } refusal)
            Skip = refusal;
    }
}
