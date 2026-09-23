using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.SupervisionPaths;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Web.SettingsWebHost;

/// <summary>
/// One <see cref="HttpListener"/>, one request at a time, every answer from <see cref="SettingsRequest_Handler"/>
/// except the page at <c>GET /</c> and the four refusals only a socket can make (caller not on this machine,
/// body too large, a method on the page other than GET, and the last-resort 500). See <see cref="ISettingsWebHost"/>.
///
/// <para>
/// THE TWO SETTINGS IT READS, AND WHEN (rulings P10, P30). <c>web.listen</c> is read ONCE, at
/// <see cref="Run_Async"/>, through the shared reading — it is <c>Restart = Host</c>, so rebinding mid-run would
/// make its restart label a lie. <c>web.token</c> is read through the writers' classifier
/// (<see cref="OrchestratorConfig_Loader.Read_TreeForEditing"/>, so an unreadable file FAILS CLOSED — see
/// <see cref="Current_Token"/>) and <see cref="Settings_Resolver"/> — never through a reading, which MASKS it (P2) — and
/// re-read whenever the config provider hands back a new instance, i.e. whenever config.json or secrets.json
/// changed: an owner who sets a token by hand must not have to restart the bridge for the page to start demanding it.
/// </para>
/// <para>
/// ONE REQUEST AT A TIME. The page is one person's settings form and every write goes through one file lock
/// anyway; serving sequentially keeps a misbehaving client from fanning out into the bridge's process.
/// </para>
/// </summary>
internal sealed class SettingsWebHostModel(
    ISupervisionPaths paths,
    IOrchestratorConfigProvider configProvider,
    IOrchestrationLog log) : ISettingsWebHost
{
    /// <summary>The machine-wide log line, not one orchestration's — the id the handler and the config loader use.</summary>
    const string GLOBAL_ORCH_ID = "";

    const string ROOT_PATH = "/";
    const string GET_METHOD = "GET";
    const string LISTEN_PATH = "web.listen";

    const string PAGE_ALLOWED_METHODS = GET_METHOD;
    const string API_ALLOWED_METHODS = "GET, PUT, DELETE";

    readonly ISupervisionPaths _paths = paths;
    readonly IOrchestratorConfigProvider _configProvider = configProvider;
    readonly IOrchestrationLog _log = log;

    readonly Lock _tokenLock = new();
    object? _tokenReadFor;
    string _token = "";

    volatile bool _isListening;
    volatile string? _listeningOn;

    public bool IsListening => _isListening;
    public string? ListeningOn_OrNull => _listeningOn;

    public async Task Run_Async(CancellationToken cancellationToken)
    {
        var bind = Decide_Bind_OrNull();

        if (bind == null)
            return;

        var (listener, prefixes) = Start_OrNull(bind.Value.Host, bind.Value.Port, bind.Value.Listen);

        if (listener == null)
            return;

        using var stop = cancellationToken.Register(() => Stop_Quietly(listener));

        _listeningOn = prefixes[0];
        _isListening = true;
        _log.Log_Info(GLOBAL_ORCH_ID, $"Settings page listening on {string.Join(" and ", prefixes)}");

        try
        {
            await Serve_Until_Async(listener, cancellationToken);
        }
        finally
        {
            _isListening = false;
            _listeningOn = null;
            Stop_Quietly(listener);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Binding
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// What to bind, or null — with the one log line saying why — when nothing should be: "off", a value that is
    /// not host:port (the catalogue's validator refuses it at write time, but config.json is hand-editable), or a
    /// non-loopback host while no token is set.
    /// </summary>
    (string Host, int Port, string Listen)? Decide_Bind_OrNull()
    {
        string? listen;

        try
        {
            listen = Read_ListenText_OrNull();
        }
        catch (Exception ex)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Settings page not started: web.listen could not be read ({ex.GetType().Name}: {ex.Message}).");
            return null;
        }

        if (ListenAddress.Is_Off(listen))
        {
            _log.Log_Info(GLOBAL_ORCH_ID, $"Settings page off (web.listen = {ListenAddress.OFF}) — no listener.");
            return null;
        }

        var address = ListenAddress.Parse_OrNull(listen);

        if (address == null)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Settings page not started: web.listen '{listen}' is not host:port or '{ListenAddress.OFF}'.");
            return null;
        }

        // Only a token that was actually READ counts as set: the fail-closed stand-in refuses edits, and must never be
        // the reason an interface gets bound (fix round 1, 2026-09-23).
        var (token, wasRead) = Current_Token();
        var refusal = SettingsWebHost_Policy.Refuse_Bind_OrNull(address.Value.Host, wasRead && SettingsSnapshot_Reader.Is_SecretSet(token));

        if (refusal != null)
        {
            _log.Log_Warning(GLOBAL_ORCH_ID, $"Settings page not started on web.listen '{listen}': {refusal}");
            return null;
        }

        return (address.Value.Host, address.Value.Port, listen!);
    }

    string? Read_ListenText_OrNull()
    {
        var value = SettingsSnapshot_Reader.Read_One_FromDisk_OrNull(LISTEN_PATH, _paths, session: null, _log)?.Value_OrNull;

        return value is JsonValue text && text.TryGetValue<string>(out var listen) ? listen : value?.ToJsonString();
    }

    /// <summary>
    /// Starts a listener on every prefix, or — when that fails and there were several — on the first alone, so a
    /// <c>localhost</c> that the OS cannot bind (an IPv6-less kernel resolving it to <c>::1</c>) does not cost the
    /// literal <c>127.0.0.1</c>. ONE warning line when nothing could be bound, naming the address and the reason:
    /// the WPF app and the daemon ship the same default port and the second one to start lands here, and it must
    /// say so once and let its bridge run.
    /// </summary>
    (HttpListener? Listener, IReadOnlyList<string> Prefixes) Start_OrNull(string host, int port, string listen)
    {
        var localhostRefusal = SettingsWebHost_Policy.Refuse_Localhost_OrNull(OperatingSystem.IsWindows(), SettingsWebHost_Policy.Resolve_Localhost_OrNull());
        var prefixes = SettingsWebHost_Policy.Build_Prefixes(host, port, includeLocalhost: localhostRefusal == null);

        if (localhostRefusal != null && SettingsWebHost_Policy.Wants_Localhost(host))
            _log.Log_Info(GLOBAL_ORCH_ID, $"Settings page: {localhostRefusal}");

        var (listener, failure) = Try_Start(prefixes);

        if (listener != null)
            return (listener, prefixes);

        if (prefixes.Count > 1)
        {
            IReadOnlyList<string> first = [prefixes[0]];
            (listener, _) = Try_Start(first);

            if (listener != null)
            {
                _log.Log_Info(GLOBAL_ORCH_ID, $"Settings page: {string.Join(", ", prefixes.Skip(1))} could not be bound ({Describe(failure!)}) — serving {prefixes[0]} only.");
                return (listener, first);
            }
        }

        _log.Log_Warning(GLOBAL_ORCH_ID, $"Settings page not started on web.listen '{listen}': {Describe_BindFailure(failure!)} The bridge keeps running.");
        return (null, prefixes);
    }

    static (HttpListener? Listener, Exception? Failure) Try_Start(IReadOnlyList<string> prefixes)
    {
        var listener = new HttpListener();

        try
        {
            foreach (var prefix in prefixes)
                listener.Prefixes.Add(prefix);

            listener.Start();
            return (listener, null);
        }
        catch (Exception ex)
        {
            Stop_Quietly(listener);
            return (null, ex);
        }
    }

    /// <summary>
    /// A Windows URL ACL is NAMED, never added: binding an interface there needs one (error 5, access denied), and
    /// adding it would need an elevated <c>netsh</c> — a machine-wide change this app has no business making.
    /// </summary>
    static string Describe_BindFailure(Exception failure)
    {
        if (failure is HttpListenerException { ErrorCode: 5 } && OperatingSystem.IsWindows())
            return $"{Describe(failure)} — on Windows a non-loopback address needs a URL ACL (netsh http add urlacl), which this app does not add.";

        return $"{Describe(failure)} — is another AI Orchestrator host (the desktop app or the daemon) already serving it?";
    }

    static string Describe(Exception failure)
    {
        return $"{failure.GetType().Name}: {failure.Message}";
    }

    static void Stop_Quietly(HttpListener listener)
    {
        try
        {
            listener.Close();
        }
        catch
        {
            // Closing a listener that failed to start, or is already closed: nothing left to release.
        }
    }

    // ---------------------------------------------------------------------------------------
    // Serving
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// How many accept failures IN A ROW the loop absorbs before it gives up. A broken connection surfaces as one
    /// <see cref="HttpListenerException"/> and the next accept succeeds; a listener that fails every accept would
    /// otherwise spin a core and write a log line per turn (fix round 1). Each failure waits a little longer.
    /// </summary>
    const int MAX_CONSECUTIVE_ACCEPT_FAILURES = 10;
    const int ACCEPT_BACKOFF_STEP_MILLISECONDS = 100;

    async Task Serve_Until_Async(HttpListener listener, CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;

        while (!cancellationToken.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await listener.GetContextAsync();
                consecutiveFailures = 0;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested || !listener.IsListening)
            {
                return;
            }
            catch (HttpListenerException ex)
            {
                // A broken connection surfaces here on some platforms; the listener itself is still up. Silent while
                // it is a blip; ONE warning, and the page stops, once it is plainly not.
                if (++consecutiveFailures >= MAX_CONSECUTIVE_ACCEPT_FAILURES)
                {
                    _log.Log_Warning(GLOBAL_ORCH_ID, $"Settings page stopped: {consecutiveFailures} requests in a row could not be accepted (last: {Describe(ex)}). The bridge keeps running.");
                    return;
                }

                try
                {
                    await Task.Delay(ACCEPT_BACKOFF_STEP_MILLISECONDS * consecutiveFailures, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }
            catch (Exception ex)
            {
                // Anything else means the listener itself is in a state this loop does not understand: stop serving,
                // say so once, and let Run_Async return normally — a spinning accept loop, or a faulted task in the
                // bridge's process, would each be worse than a settings page that went away.
                _log.Log_Warning(GLOBAL_ORCH_ID, $"Settings page stopped: the listener failed ({Describe(ex)}). The bridge keeps running.");
                return;
            }

            await Serve_Async(context, cancellationToken);
        }
    }

    /// <summary>
    /// ONE REQUEST, AND NO EXCEPTION EVER LEAVES IT (Task 7 carry). The handler is written never to throw for
    /// anything a client sends, but its GET reads config.json, and a config.json holding a lone-surrogate escape
    /// still makes that read throw (Task 1's reader, 2026-09-23): the last-resort catch turns it into a 500 and a
    /// log line, never a dead listener or an unobserved task in the bridge's process.
    /// </summary>
    async Task Serve_Async(HttpListenerContext context, CancellationToken cancellationToken)
    {
        (int Status, string ContentType, string Body) answer;

        try
        {
            answer = await Answer_Async(context.Request, cancellationToken);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping and closed the listener under an in-flight read: not a failure of the bridge, and
            // nobody is left to answer (fix round 1 — this used to log an error on every ordinary shutdown mid-request).
            Close_Quietly(context.Response);
            return;
        }
        catch (Exception ex)
        {
            _log.Log_Error(GLOBAL_ORCH_ID, "Settings page: a request failed inside the bridge — answered 500", ex);
            answer = Answer_Error(HttpStatusCode.InternalServerError, "The bridge failed while answering this request; the reason is in its log. Nothing was changed by it.");
        }

        try
        {
            var isPage = string.Equals((context.Request.RawUrl ?? ROOT_PATH).Split('?', 2)[0], ROOT_PATH, StringComparison.Ordinal);

            Write(context.Response, answer, isPage ? PAGE_ALLOWED_METHODS : API_ALLOWED_METHODS);
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The client went away mid-answer: nothing to tell it, and nothing the bridge needs to know.
        }
        finally
        {
            Close_Quietly(context.Response);
        }
    }

    static void Close_Quietly(HttpListenerResponse response)
    {
        try
        {
            response.Close();
        }
        catch
        {
            // Already closed or aborted by the client, or the listener was closed under it.
        }
    }

    async Task<(int Status, string ContentType, string Body)> Answer_Async(HttpListenerRequest request, CancellationToken cancellationToken)
    {
        if (!SettingsWebHost_Policy.Is_LoopbackCaller(request.RemoteEndPoint?.Address))
            return Answer_Error(HttpStatusCode.Forbidden, Describe_NotThisMachine(request.RemoteEndPoint?.Address));

        var target = request.RawUrl ?? ROOT_PATH;
        var route = target.Split('?', 2)[0];
        Func<string, string?> header = name => request.Headers[name];

        if (string.Equals(route, ROOT_PATH, StringComparison.Ordinal))
            return Answer_Page(request.HttpMethod, header);

        var (body, tooLarge) = await Read_Body_Async(request, cancellationToken);

        if (tooLarge)
            return Answer_Error(HttpStatusCode.RequestEntityTooLarge, $"The body is larger than {SettingsWebHost_Policy.MAX_BODY_BYTES} bytes — nothing was read or changed. Send one setting at a time.");

        return SettingsRequest_Handler.Handle(request.HttpMethod, target, header, body, _paths, Current_Token().Token, _log);
    }

    /// <summary>
    /// <c>GET /</c> is the page, held to the SAME Host rule as the API (Task 7 carry) — a DNS-rebinding site
    /// could otherwise read it — by asking <see cref="SettingsRequest_Handler.Is_LoopbackHost"/>, and for the
    /// refusal the handler itself, whose Host check runs before its route: one rule, one message.
    /// </summary>
    (int Status, string ContentType, string Body) Answer_Page(string method, Func<string, string?> header)
    {
        if (!SettingsRequest_Handler.Is_LoopbackHost(header(SettingsRequest_Handler.HOST_HEADER)))
            return SettingsRequest_Handler.Handle(method, ROOT_PATH, header, string.Empty, _paths, string.Empty, _log);

        if (!string.Equals(method, GET_METHOD, StringComparison.Ordinal))
            return Answer_Error(HttpStatusCode.MethodNotAllowed, $"{method} is not supported on {ROOT_PATH}, which is the page — allowed: {GET_METHOD}. The API is {SettingsRequest_Handler.SETTINGS_PATH}.");

        return ((int)HttpStatusCode.OK, SettingsPage_Reader.CONTENT_TYPE, SettingsPage_Reader.Read_Html());
    }

    /// <summary>
    /// The body as UTF-8 text, or "too large" without buffering it: a declared length over the cap is refused
    /// before a byte is read, and an undeclared (chunked) one is read only until it passes the cap.
    /// </summary>
    static async Task<(string Body, bool TooLarge)> Read_Body_Async(HttpListenerRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasEntityBody)
            return (string.Empty, false);

        if (request.ContentLength64 > SettingsWebHost_Policy.MAX_BODY_BYTES)
            return (string.Empty, true);

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];

        while (true)
        {
            var read = await request.InputStream.ReadAsync(chunk, cancellationToken);

            if (read == 0)
                break;

            buffer.Write(chunk, 0, read);

            if (buffer.Length > SettingsWebHost_Policy.MAX_BODY_BYTES)
                return (string.Empty, true);
        }

        return (Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length), false);
    }

    /// <summary>
    /// NEVER an <c>Access-Control-Allow-*</c> header (Task 7 carry): the handler's refusal of OPTIONS is the CSRF
    /// defence while editing is open (D4), and one CORS header here would undo it for every site the owner's
    /// browser visits. <c>X-Frame-Options</c> and <c>frame-ancestors</c> keep another site from framing the page
    /// (clickjacking); <c>Allow</c> is what HTTP asks of a 405, which the handler's tuple has no room for.
    /// </summary>
    static void Write(HttpListenerResponse response, (int Status, string ContentType, string Body) answer, string allowedMethods)
    {
        var bytes = Encoding.UTF8.GetBytes(answer.Body);

        response.StatusCode = answer.Status;
        response.ContentType = answer.ContentType;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "DENY";
        response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
        response.Headers["Referrer-Policy"] = "no-referrer";

        if (answer.Status == (int)HttpStatusCode.MethodNotAllowed)
            response.Headers["Allow"] = allowedMethods;

        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes);
    }

    static (int Status, string ContentType, string Body) Answer_Error(HttpStatusCode status, string message)
    {
        return ((int)status, SettingsRequest_Handler.CONTENT_TYPE, new JsonObject { ["error"] = message }.ToJsonString(JsonWriting.INDENTED));
    }

    static string Describe_NotThisMachine(IPAddress? address)
    {
        var from = address == null ? "an unknown address" : address.ToString();

        return $"This page answers only callers on this machine; this request came from {from}. "
            + "From another machine, reach it through an SSH tunnel to its loopback address.";
    }

    // ---------------------------------------------------------------------------------------
    // The token
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The raw <c>web.token</c> and whether it was actually READ. Re-read only when the provider's instance changed
    /// (ruling P10) — and a FAILED read is never cached, so the next request tries again the moment the file reads.
    ///
    /// <para>
    /// A TOKEN THAT CANNOT BE READ FAILS CLOSED (fix round 1, 2026-09-23). The first version read config.json through
    /// the snapshot reader's tolerant read, which turns a held, half-rewritten, truncated or garbage file into
    /// <c>{}</c> — a token of <c>""</c>, cached until the stamp moved, and token-less edits of every unfenced row open
    /// meanwhile, on a machine whose owner HAD set a token. It now reads through the writers' own classifier,
    /// <see cref="OrchestratorConfig_Loader.Read_TreeForEditing"/>: ABSENT is "no token" (a fresh machine, D4's open
    /// editing); UNREADABLE or CORRUPT (empty included) stands in a random secret nobody holds, so every edit is a
    /// 401 until the file reads again. secrets.json takes no part: <c>web.token</c> is a config.json key.
    /// </para>
    /// </summary>
    (string Token, bool WasRead) Current_Token()
    {
        IOrchestratorConfig? current = null;

        try
        {
            current = _configProvider.Get_Current();
        }
        catch
        {
            // The provider logs its own failures; with no instance to compare, the token is read every time.
        }

        lock (_tokenLock)
        {
            if (current != null && ReferenceEquals(current, _tokenReadFor))
                return (_token, true);

            var token = Read_Token_OrNull();

            if (token == null)
            {
                _tokenReadFor = null;
                return (FAIL_CLOSED_TOKEN, false);
            }

            _tokenUnreadableReported = false;
            _token = token;
            _tokenReadFor = current;

            return (_token, true);
        }
    }

    /// <summary>
    /// A secret nobody holds, drawn once per process: what the handler is given while the token cannot be read. Never
    /// logged, never shown, never compared except by the handler's fixed-time check, where nothing can match it.
    /// </summary>
    static readonly string FAIL_CLOSED_TOKEN = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>Set while the token is unreadable, so a failure is logged once and not once per request.</summary>
    bool _tokenUnreadableReported;

    /// <summary>The token (<c>""</c> when none is stated), or null when config.json is there and cannot be read or parsed.</summary>
    string? Read_Token_OrNull()
    {
        try
        {
            var definition = Catalog.Find_OrNull(SettingsSnapshot_Reader.MASKED_SECRET_PATH)
                ?? throw new InvalidOperationException($"The catalogue has no '{SettingsSnapshot_Reader.MASKED_SECRET_PATH}' row.");

            var (configTree, refusal) = OrchestratorConfig_Loader.Read_TreeForEditing(_paths.ConfigFile, log: null, corruptReadsAsEmpty: false);

            if (configTree == null)
            {
                Report_Unreadable(refusal ?? "config.json could not be read");
                return null;
            }

            var (presetTree, _) = OrchestratorConfig_Loader.Resolve_Preset_OrClassic(configTree, _log);
            var (value, _) = Settings_Resolver.Resolve(definition, presetTree, configTree, session: null);

            // A hand-written non-string ("token": 12345) is still a token the owner meant to set: its JSON text is
            // the secret, rather than a value that silently reads as "not set" and opens editing.
            return value switch
            {
                null => string.Empty,
                JsonValue text when text.TryGetValue<string>(out var token) => token,
                _ => value.ToJsonString(),
            };
        }
        catch (Exception ex)
        {
            Report_Unreadable(Describe(ex));
            return null;
        }
    }

    void Report_Unreadable(string reason)
    {
        if (_tokenUnreadableReported)
            return;

        _tokenUnreadableReported = true;
        _log.Log_Warning(GLOBAL_ORCH_ID, $"Settings page: web.token could not be read ({reason}) — every edit is refused until config.json reads again.");
    }
}
