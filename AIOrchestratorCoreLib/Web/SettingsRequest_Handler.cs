using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingReading;
using AIOrchestratorCoreLib.Configuration.SettingsWriting;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.SupervisionPaths;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Web;

/// <summary>
/// THE SETTINGS WEB PAGE'S WHOLE API, AS A PURE FUNCTION OF ONE REQUEST (plan 04 Task 6, spec §8.3). Every
/// decision about a request — which route, which method, whether the token is needed and right, what the
/// body means, what the answer says — is made here, and the listener of Task 7 only moves bytes between a
/// socket and <see cref="Handle"/>. That split is what lets the web renderer be tested as thoroughly as the
/// Telegram one without a socket, and it is the reason nothing here touches <c>HttpListener</c>.
///
/// <para>
/// IT DECIDES NOTHING ABOUT A SETTING. The GET body is <see cref="SettingsSnapshot_Reader"/>'s reading laid
/// out in <see cref="SettingsRow_Builder"/>'s sections — the same reading the Telegram menu and the WPF
/// window draw — and a PUT or DELETE is handed whole to <see cref="Settings_Writer"/>, whose refusal messages
/// are the catalogue definition's own words (CLAUDE.md decisions 12 and 21). The messages this class writes
/// itself are about the REQUEST — not JSON, no such route, wrong token — never about a value.
/// </para>
///
/// <para>THE WIRE SHAPE — what the page (Task 8) consumes; every response is <see cref="CONTENT_TYPE"/>.</para>
/// <code>
/// GET /settings  → 200
/// {
///   "preset":  "classic",                          // the preset the snapshot resolved under (D13: shown, never a row)
///   "editing": { "tokenRequired": false,           // true once web.token is set (D4)
///                "tokenHeader": "X-Aiorch-Token" },
///   "sections": [                                  // SettingsRow_Builder order; every category, Kit included even empty
///     { "category": "Models", "title": "Models and effort",
///       "rows": [
///         { "path": "effort.supervisor",           // what a PUT names and a DELETE resets
///           "label": "…", "description": "…",
///           "kind": "Enum",                        // SettingKinds name
///           "renderer": "Choice",                  // SettingRenderers name: Toggle | Choice | Number | Text | OrderedList | ReadOnly
///           "scope": "Orchestration",              // SettingScopes name
///           "restart": "NextSpawn", "restartLabel": "…",
///           "minimum": null, "maximum": null,      // an Int row's bounds — for drawing the box, never for judging it
///           "value": "xhigh",                      // the resolved JSON value; null for web.token, which is masked (P2)
///           "displayValue": "xhigh",               // SettingValue_Formatter's one line
///           "origin": "Preset", "originLabel": "from preset classic",
///           "editable": true,                      // Renderer != ReadOnly — the writer's own rule (P4)
///           "fenced": false,                       // true: this page may change it only once web.token is set (P32)
///           "sessionNote": null,                   // always null here: the page reads the machine, not a session
///           "offers": [ { "caption": "xhigh", "value": "xhigh" },
///                       { "caption": "not set", "value": null } ] } ] } ]
/// }
///
/// PUT /settings              body: { "&lt;path&gt;": &lt;JSON value&gt;, … }   (a partial object; null is a VALUE, P3)
/// DELETE /settings?path=&lt;p&gt;   Reset: deletes the key, writes nothing in its place (spec §6.2)
///   → 200 when every edit took effect, 422 when any was refused (the others in the same body WERE applied —
///     the writer's rule), and in both cases:
/// { "results": [ { "path": "&lt;as sent&gt;",
///                  "outcome": "Applied" | "Reset" | "RefusedUnknownPath" | "RefusedReadOnly" | "RefusedInvalid",
///                  "message": null | "&lt;the definition's own words&gt;" } ] }
///
/// Anything else → { "error": "…" } with 400 (unreadable body, DELETE without ?path=), 401 (web.token set and
/// the header absent or wrong), 403 (web.token empty and the request touches a fenced row — nothing applied),
/// 404 (not /settings), 405 (not GET/PUT/DELETE), 421 (Host is not a loopback name — checked before anything
/// else), 500 (config.json could not be written, or the writer refused it because it could not be read or does
/// not parse — the writer's WriteFailed, never a "results" outcome; nothing in that request was applied, and the
/// file is as it was).
/// </code>
///
/// <para>
/// OPEN ON LOOPBACK MUST NOT MEAN "ANY CALLER CAN MAKE THE BRIDGE RUN A COMMAND" (ruling P32, 2026-09-23). D4
/// keeps reading and editing open while <c>web.token</c> is empty, and two guards keep that from reaching
/// further than the owner meant: every request must be addressed to a loopback NAME (<see cref="Is_LoopbackHost"/>
/// — the DNS-rebinding defence, independent of how the listener registers its prefixes), and a token-less
/// PUT or DELETE may not touch a row in <see cref="FENCED_PATHS"/> — rows whose values the bridge executes, the
/// two that govern this door itself, and the three that decide which human and which chat the bridge obeys.
/// </para>
///
/// <para>
/// AN OFFER CARRIES ITS VALUE, NOT ONLY ITS CAPTION. A Toggle's "on" is <c>true</c> and a nullable Choice's
/// "not set" is JSON <c>null</c>; each is the caption read back through <see cref="SettingValue_Parser"/>, the
/// formatter's inverse for every offer (ruling P13), so the page PUTs what it was handed and never learns a
/// phrase. Without it the page's script would have to know that "not set" means null — the formatter's
/// knowledge copied into JavaScript, where no test can reach it. A picker list's offers are WORDS the page adds
/// to the list, each the JSON string itself. A Number or Text box has no offers: the page sends a JSON number
/// or string, null for an emptied box, and the definition refuses what it will not take, in its own words.
/// </para>
/// <para>
/// INVARIANT BY CONSTRUCTION. The daemon runs with InvariantGlobalization and the WPF host under the owner's
/// Italian culture; nothing here formats or parses a number through the current culture — the body is JSON
/// (culture-free by definition), and every display string comes from the invariant formatter — so one PUT
/// means the same thing in both hosts. <c>SettingsRequestHandlerTests.ANumber_IsParsedAndRenderedInvariantly_InEitherHost</c>
/// pins it under it-IT, where a culture-aware "1.5" is fifteen.
/// </para>
/// </summary>
public static class SettingsRequest_Handler
{
    /// <summary>The one route this API answers. <c>GET /</c> is the page itself, served by Task 7's listener before it asks here.</summary>
    public const string SETTINGS_PATH = "/settings";

    /// <summary>The header a PUT or DELETE carries <c>web.token</c> in, once the owner has set one.</summary>
    public const string TOKEN_HEADER = "X-Aiorch-Token";

    /// <summary>The query key naming the row a DELETE resets: <c>DELETE /settings?path=&lt;p&gt;</c> (ruling P3).</summary>
    public const string RESET_QUERY_KEY = "path";

    public const string CONTENT_TYPE = "application/json; charset=utf-8";

    const string GET_METHOD = "GET";
    const string PUT_METHOD = "PUT";
    const string DELETE_METHOD = "DELETE";

    /// <summary>Named in every 405, since the tuple this returns has no room for an <c>Allow</c> header.</summary>
    const string ALLOWED_METHODS = GET_METHOD + ", " + PUT_METHOD + ", " + DELETE_METHOD;

    /// <summary>The machine-wide log line, not one orchestration's — the same id the config loader's own warnings use.</summary>
    const string GLOBAL_ORCH_ID = "";

    public const string HOST_HEADER = "Host";

    /// <summary>
    /// THE ONLY NAMES A REQUEST MAY BE ADDRESSED TO (ruling P32a), each with or without a port. A DNS-rebinding
    /// page — a site whose name is made to resolve to 127.0.0.1 once the owner's browser has loaded it — is
    /// SAME-ORIGIN to that browser, so neither CORS nor the refused OPTIONS stops its PUT; what it cannot change
    /// is the Host header, which still carries its own name. Checked here rather than left to the listener's
    /// prefix matching, so it holds however Task 7 registers its prefixes and is testable without a socket. The
    /// PORT is deliberately not compared: an SSH tunnel's local end (<c>ssh -L 8080:127.0.0.1:7391</c>) arrives
    /// as <c>localhost:8080</c>. Consequence, stated: a non-loopback <c>web.listen</c> serves nothing but 421s —
    /// the page is reached from another machine through a tunnel whose far end is loopback, which is spec §8.3's
    /// "deliberate limit".
    /// </summary>
    public static readonly IReadOnlyList<string> LOOPBACK_HOST_NAMES = ["127.0.0.1", "localhost", "[::1]"];

    /// <summary>
    /// THE ROWS A TOKEN-LESS EDIT MAY NOT TOUCH (rulings P32b and P34, 2026-09-23) — ONE list: rows whose value
    /// the bridge executes, the listener's own two rows, and the three rows that decide which human and which
    /// chat the bridge obeys (D2's phone fence, same reason). While <c>web.token</c> is empty an edit is open to
    /// anything that can reach the loopback port: on the owner's brother's VPS that is every OS account on the
    /// machine, which config.json's own file permissions keep out. For most rows that is D4's accepted cost; for
    /// these it would be handing over the machine:
    /// <list type="bullet">
    /// <item><c>voiceTranscribeCommand</c> — a command line the bridge SHELLS OUT to with every voice note
    /// (<c>VoiceTranscriberModel</c>, through <c>ShellCommand_Builder</c>). Setting it is running a command as
    /// the owner.</item>
    /// <item><c>web.listen</c> — would move this listener off loopback onto a reachable interface.</item>
    /// <item><c>web.token</c> — the first caller would pick the secret and lock the owner out of their own page.</item>
    /// <item><c>telegramOwnerUserId</c>, <c>telegramSupergroupChatId</c>, <c>telegramInbound</c> — who the owner is,
    /// which chat the bridge answers, and whether this host listens at all. Rewriting them makes another
    /// person the owner of every agent the bridge drives — command execution by another route — or cuts the
    /// real owner off. The same three keys D2 already refuses on the phone, for the same reason (P34).</item>
    /// </list>
    /// NOT FENCED, and why, from a read of <c>SettingsCatalog.cs</c> on 2026-09-23: the six <c>models.*</c> rows
    /// reach the command line as an ARGUMENT and their validator (<c>SettingValidators.MODEL_WORD</c>) admits
    /// letters, digits, '-', '_' and '.' only; <c>runners.sessionMemoryMax</c> is one argv element to
    /// <c>systemd-run</c>, and only after <c>MemorySize_Parser</c> has read it as a size; <c>repos</c> and
    /// <c>planBackend</c> are ReadOnly, refused by the writer anyway; no row names an executable path (the
    /// <c>claude</c> binary is hard-coded in <c>ClaudeInvocation_Resolver</c>). <c>highRiskPatterns</c> stays
    /// open by ruling (P34): emptying it weakens a guardrail but grants nothing by itself.
    /// Matched by the catalogue row a path RESOLVES to, so a legacy spelling cannot walk around the fence.
    /// </summary>
    public static readonly IReadOnlyList<string> FENCED_PATHS =
    [
        "voiceTranscribeCommand",
        "web.listen",
        "web.token",
        "telegramInbound",
        "telegramSupergroupChatId",
        "telegramOwnerUserId",
    ];

    /// <summary>
    /// A REPEATED KEY IS REFUSED, NOT LAST-WINS. <c>{"x":30,"x":45}</c> is two answers to one question, and
    /// which the owner meant is not this class's to guess; refusing it at the parse is also what keeps a
    /// duplicate from surfacing later as a throw inside a lazily built <see cref="JsonObject"/>.
    /// </summary>
    static readonly JsonDocumentOptions PUT_BODY_OPTIONS = new() { AllowDuplicateProperties = false };

    /// <summary>
    /// One request, one answer, and never a throw for anything a client can send. <paramref name="path"/> is the
    /// request target as it arrived — the path and an optional query, still percent-encoded
    /// (<c>HttpListenerRequest.RawUrl</c>); <paramref name="header"/> looks a request header up by name, and
    /// <see cref="HOST_HEADER"/> is one of the headers it must answer. The checks run in this order, so a refusal
    /// never follows a partial effect: Host (421), route (404), method (405), token (401), body (400), fence
    /// (403), and only then the writer.
    ///
    /// <para>
    /// <paramref name="configuredToken"/> IS HANDED IN, NEVER READ HERE (ruling P30). The reading every renderer
    /// shares masks <c>web.token</c> (ruling P2), so this class could not read it through a reading if it tried;
    /// the host resolves the raw value through <c>SettingsSnapshot_Reader.Read_Trees_FromDisk</c> and the
    /// resolver, and the mask stays a property of every reading without an exception carved into it.
    /// </para>
    /// </summary>
    public static (int Status, string ContentType, string Body) Handle(
        string method,
        string path,
        Func<string, string?> header,
        string body,
        ISupervisionPaths paths,
        string configuredToken,
        IOrchestrationLog? log)
    {
        // FIRST, before the route: a request addressed to another name gets nothing from here, not even a 404.
        var host = header(HOST_HEADER);

        if (!Is_LoopbackHost(host))
            return Answer_Error(HttpStatusCode.MisdirectedRequest, Describe_NotLoopback(host));

        var (route, query) = Split_Target(path);

        if (!string.Equals(route, SETTINGS_PATH, StringComparison.Ordinal))
            return Answer_Error(HttpStatusCode.NotFound, Describe_NotFound(route));

        return method switch
        {
            GET_METHOD => Answer_Get(paths, configuredToken, log),
            PUT_METHOD => Refuse_Unauthorised_OrNull(header, configuredToken) ?? Answer_Put(body, paths, configuredToken, log),
            DELETE_METHOD => Refuse_Unauthorised_OrNull(header, configuredToken) ?? Answer_Delete(query, paths, configuredToken, log),

            // OPTIONS lands here ON PURPOSE. A PUT or DELETE from a page on another origin is never a "simple"
            // request, so the browser sends a CORS preflight first; a 405 carrying no Access-Control-Allow-*
            // is what stops a website open in the owner's browser from rewriting config.json over loopback
            // while editing is open (D4). Answering OPTIONS here would open that door.
            _ => Answer_Error(HttpStatusCode.MethodNotAllowed, Describe_MethodNotAllowed(method)),
        };
    }

    // ---------------------------------------------------------------------------------------
    // GET
    // ---------------------------------------------------------------------------------------

    static (int Status, string ContentType, string Body) Answer_Get(ISupervisionPaths paths, string configuredToken, IOrchestrationLog? log)
    {
        var (readings, presetName) = SettingsSnapshot_Reader.Read_All_FromDisk(paths, session: null, log);

        var sections = new JsonArray();

        foreach (var (category, title, rows) in SettingsRow_Builder.Build_Sections(readings))
            sections.Add(Build_SectionJson(category, title, rows));

        return Answer(HttpStatusCode.OK, new JsonObject
        {
            ["preset"] = presetName,
            ["editing"] = new JsonObject
            {
                ["tokenRequired"] = SettingsSnapshot_Reader.Is_SecretSet(configuredToken),
                ["tokenHeader"] = TOKEN_HEADER,
            },
            ["sections"] = sections,
        });
    }

    static JsonObject Build_SectionJson(SettingCategories category, string title, IReadOnlyList<ISettingReading> rows)
    {
        var rowsJson = new JsonArray();

        foreach (var reading in rows)
            rowsJson.Add(Build_RowJson(reading));

        return new JsonObject
        {
            ["category"] = category.ToString(),
            ["title"] = title,
            ["rows"] = rowsJson,
        };
    }

    /// <summary>
    /// The reading, member for member, plus the catalogue facts a renderer draws with. Nothing is derived here:
    /// every string is the reading's or the definition's, so this renderer cannot print a different word from
    /// the other two for the same row (Task 10 asserts that across all three).
    /// </summary>
    static JsonObject Build_RowJson(ISettingReading reading)
    {
        var definition = reading.Definition;

        return new JsonObject
        {
            ["path"] = definition.Path,
            ["label"] = definition.Label,
            ["description"] = definition.Description,
            ["kind"] = definition.Kind.ToString(),
            ["renderer"] = definition.Renderer.ToString(),
            ["scope"] = definition.Scope.ToString(),
            ["restart"] = definition.Restart.ToString(),
            ["restartLabel"] = reading.RestartLabel,
            ["minimum"] = definition.Minimum,
            ["maximum"] = definition.Maximum,

            // A fresh copy on every read (ISettingReading's contract), so attaching it here re-parents nothing shared.
            ["value"] = reading.Value_OrNull,
            ["displayValue"] = reading.DisplayValue,
            ["origin"] = reading.Origin.ToString(),
            ["originLabel"] = reading.OriginLabel,
            ["editable"] = reading.IsEditable,

            // Read off the one list, so the page greys out what a token-less PUT would be refused rather than
            // carrying a second copy of FENCED_PATHS in its script.
            ["fenced"] = Is_Fenced(definition),
            ["sessionNote"] = reading.SessionNote_OrNull,
            ["offers"] = Build_OffersJson(reading),
        };
    }

    static JsonArray Build_OffersJson(ISettingReading reading)
    {
        var offers = new JsonArray();

        foreach (var caption in reading.OfferedValues)
        {
            offers.Add(new JsonObject
            {
                ["caption"] = caption,
                ["value"] = Read_OfferValue_OrNull(reading.Definition, caption),
            });
        }

        return offers;
    }

    /// <summary>
    /// What a PUT should carry for an offer. A picker list offers WORDS to add, and a word of a string list is
    /// the JSON string itself — handing it to the parser would read it as a whole one-word list. Every other
    /// offer is a whole value, read back through the one parser (see the class doc).
    /// </summary>
    static JsonNode? Read_OfferValue_OrNull(ISettingDefinition definition, string caption)
    {
        return definition.Renderer == SettingRenderers.OrderedList
            ? JsonValue.Create(caption)
            : SettingValue_Parser.Parse(definition, caption);
    }

    // ---------------------------------------------------------------------------------------
    // PUT and DELETE
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// D4 (owner, 2026-09-14), ruling P1: EDITING IS OPEN ON LOOPBACK UNTIL <c>web.token</c> IS SET. The default
    /// listener is <c>127.0.0.1:7391</c>, reached from another machine only through an SSH tunnel, and the owner
    /// chose a page that edits from the first run over one more step on a fresh machine. A SET token is still
    /// enforced, for PUT and DELETE alike — both rewrite config.json.
    ///
    /// <para>
    /// FIXED-TIME, AND OVER DIGESTS. It is a shared secret guarding a page that rewrites config.json, and a
    /// naive <c>==</c> returns at the first differing character — a timing side channel for no reason.
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> still returns early on a LENGTH mismatch, so both
    /// sides are hashed first: the comparison is always 32 bytes against 32 bytes, and the token's length is
    /// not measurable either.
    /// </para>
    /// </summary>
    static (int Status, string ContentType, string Body)? Refuse_Unauthorised_OrNull(Func<string, string?> header, string configuredToken)
    {
        if (!SettingsSnapshot_Reader.Is_SecretSet(configuredToken))
            return null;

        var presented = header(TOKEN_HEADER);

        if (presented != null && Tokens_Match(presented, configuredToken))
            return null;

        return Answer_Error(HttpStatusCode.Unauthorized, Describe_Unauthorised());
    }

    static bool Tokens_Match(string presented, string configured)
    {
        var presentedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        var configuredDigest = SHA256.HashData(Encoding.UTF8.GetBytes(configured));

        return CryptographicOperations.FixedTimeEquals(presentedDigest, configuredDigest);
    }

    /// <summary>
    /// A TOKEN-LESS EDIT THAT TOUCHES A FENCED ROW IS REFUSED WHOLE (ruling P32b): 403, and nothing in the
    /// request is applied — not even its unfenced edits, because a body that was half-written and half-refused
    /// would leave the caller to work out which half landed, and "nothing" is the one answer that needs no
    /// reading. Null when a token is set (the 401 check has already passed by then) or no edit is fenced.
    /// </summary>
    static (int Status, string ContentType, string Body)? Refuse_Fenced_OrNull(IEnumerable<string> editedPaths, string configuredToken)
    {
        if (SettingsSnapshot_Reader.Is_SecretSet(configuredToken))
            return null;

        var touched = editedPaths
            .Select(Catalog.Find_OrNull)
            .Where(definition => definition != null && Is_Fenced(definition))
            .Select(definition => definition!.Path)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return touched.Length == 0 ? null : Answer_Error(HttpStatusCode.Forbidden, Describe_Fenced(touched));
    }

    /// <summary>By the row's own path, whatever spelling the request used to reach it.</summary>
    static bool Is_Fenced(ISettingDefinition definition)
    {
        return FENCED_PATHS.Contains(definition.Path, StringComparer.Ordinal);
    }

    /// <summary>
    /// Whether a Host header names this machine's loopback (<see cref="LOOPBACK_HOST_NAMES"/>, any case — a host
    /// name is case-insensitive), with no port or a port of digits. Absent is refused: every browser sends Host,
    /// and HTTP/1.1 requires it, so a request without one is not the page's. Public so Task 7 can hold <c>GET /</c>
    /// to the same rule instead of writing a second copy of it.
    /// </summary>
    public static bool Is_LoopbackHost(string? hostHeader)
    {
        if (string.IsNullOrWhiteSpace(hostHeader))
            return false;

        var (name, port) = Split_HostHeader(hostHeader.Trim());

        if (port != null && (port.Length is 0 or > 5 || !port.All(char.IsAsciiDigit)))
            return false;

        return LOOPBACK_HOST_NAMES.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// "name[:port]", where an IPv6 name keeps its brackets so its own colons are never read as the port's. Null
    /// port when there is none; an empty one ("localhost:") is returned empty, for the caller to refuse.
    /// </summary>
    static (string Name, string? Port) Split_HostHeader(string host)
    {
        if (host.StartsWith('['))
        {
            var close = host.IndexOf(']');

            if (close < 0)
                return (host, null);

            var bracketed = host[..(close + 1)];
            var rest = host[(close + 1)..];

            if (rest.Length == 0)
                return (bracketed, null);

            // Anything after ']' that is not ":port" ("[::1]7391") leaves the whole header as the name, which matches nothing.
            return rest.StartsWith(':') ? (bracketed, rest[1..]) : (host, null);
        }

        var colon = host.LastIndexOf(':');

        return colon < 0 ? (host, null) : (host[..colon], host[(colon + 1)..]);
    }

    static (int Status, string ContentType, string Body) Answer_Put(string body, ISupervisionPaths paths, string configuredToken, IOrchestrationLog? log)
    {
        var (edits, problem) = Parse_PutBody(body);

        if (edits == null)
            return Answer_Error(HttpStatusCode.BadRequest, problem ?? Describe_NotAnObject());

        var fenced = Refuse_Fenced_OrNull(edits.Select(edit => edit.Path), configuredToken);

        if (fenced != null)
            return fenced.Value;

        try
        {
            var results = Settings_Writer.Apply_Many(paths, edits, log);

            return Refuse_WriteFailed_OrNull(results) ?? Answer_Results(results);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Answer_WriteFailed(ex, log);
        }
    }

    /// <summary>
    /// The body as edits, in the order sent, or the reason it is not a body this API reads. A parser of
    /// untrusted input: it swallows the parser's exception and returns the reason, because a client's bad
    /// body is an answer to give, not a fault to raise.
    ///
    /// <para>
    /// EVERY KEY AND EVERY STRING IS DECODED HERE, INSIDE THE TRY (task-6 fix round 1, 2026-09-23). A JSON
    /// escape can spell a lone surrogate — <c>{"\uD800":1}</c> is twelve plain-ASCII bytes — and System.Text.Json
    /// accepts it at the parse and throws <see cref="InvalidOperationException"/> only when the text is
    /// finally read as a .NET string: the key when the object is first enumerated, a string value when the
    /// definition's validator asks for it, far from here and outside any catch. Verified against the built DLL
    /// by the review of <c>649a38c</c>: the bridge host that Task 7 runs this in would have taken that throw
    /// from one request. Reading everything once, now, makes the lazy throw an eager one, where it is a 400.
    /// </para>
    /// </summary>
    static (IReadOnlyList<(string Path, JsonNode? Value)>? Edits_OrNull, string? Problem_OrNull) Parse_PutBody(string body)
    {
        JsonNode? root;

        try
        {
            root = JsonNode.Parse(body, nodeOptions: null, PUT_BODY_OPTIONS);
            Decode_EveryString(root);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            return (null, Describe_NotJson(ex.Message));
        }

        if (root is not JsonObject edits)
            return (null, Describe_NotAnObject());

        if (edits.Count == 0)
            return (null, Describe_NoEdits());

        return (edits.Select(edit => (edit.Key, edit.Value)).ToArray(), null);
    }

    /// <summary>Reads every property name and every string value as a .NET string once, so any that cannot be one throws HERE.</summary>
    static void Decode_EveryString(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, value) in obj)
                    Decode_EveryString(value);

                break;

            case JsonArray array:
                foreach (var element in array)
                    Decode_EveryString(element);

                break;

            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                _ = value.GetValue<string>();
                break;
        }
    }

    static (int Status, string ContentType, string Body) Answer_Delete(string query, ISupervisionPaths paths, string configuredToken, IOrchestrationLog? log)
    {
        var settingPath = Read_QueryValue_OrNull(query, RESET_QUERY_KEY);

        if (string.IsNullOrWhiteSpace(settingPath))
            return Answer_Error(HttpStatusCode.BadRequest, Describe_ResetNeedsAPath());

        var fenced = Refuse_Fenced_OrNull([settingPath], configuredToken);

        if (fenced != null)
            return fenced.Value;

        try
        {
            var (outcome, message) = Settings_Writer.Reset(paths, settingPath, log);
            IReadOnlyList<(string Path, SettingsWriteOutcomes Outcome, string? Message_OrNull)> results = [(settingPath, outcome, message)];

            return Refuse_WriteFailed_OrNull(results) ?? Answer_Results(results);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Answer_WriteFailed(ex, log);
        }
    }

    /// <summary>
    /// Every result, in the order the body named them, each with the path AS SENT — a legacy spelling
    /// included — so the page can answer its own body key by key. The status is only a summary of them.
    /// </summary>
    static (int Status, string ContentType, string Body) Answer_Results(
        IReadOnlyList<(string Path, SettingsWriteOutcomes Outcome, string? Message_OrNull)> results)
    {
        var resultsJson = new JsonArray();
        var everyEditTookEffect = true;

        foreach (var (path, outcome, message) in results)
        {
            resultsJson.Add(new JsonObject
            {
                ["path"] = path,
                ["outcome"] = outcome.ToString(),
                ["message"] = message,
            });

            everyEditTookEffect &= Took_Effect(outcome);
        }

        var status = everyEditTookEffect ? HttpStatusCode.OK : HttpStatusCode.UnprocessableContent;

        return Answer(status, new JsonObject { ["results"] = resultsJson });
    }

    static bool Took_Effect(SettingsWriteOutcomes outcome)
    {
        return outcome switch
        {
            SettingsWriteOutcomes.Applied or SettingsWriteOutcomes.Reset => true,
            SettingsWriteOutcomes.RefusedUnknownPath or SettingsWriteOutcomes.RefusedReadOnly or SettingsWriteOutcomes.RefusedInvalid => false,

            // Never reached — Refuse_WriteFailed_OrNull answers it as a 500 first — and honest if it ever were.
            SettingsWriteOutcomes.WriteFailed => false,
            _ => throw new InvalidOperationException($"Unhandled SettingsWriteOutcomes: {outcome}"),
        };
    }

    /// <summary>
    /// THE WRITER THREW, SO NOTHING WAS WRITTEN — and the answer says exactly that (<c>Atomic_FileWriter</c>: a
    /// write that did not happen is never reported as one; the writer writes a whole body in ONE rename, so
    /// "nothing in this request" is literal). One line to the log as well (decision 15: not Telegram), because
    /// a config.json the app cannot write is the owner's problem beyond this one page.
    /// </summary>
    static (int Status, string ContentType, string Body) Answer_WriteFailed(Exception ex, IOrchestrationLog? log)
    {
        var message = Describe_WriteFailed(ex.Message);

        log?.Log_Error(GLOBAL_ORCH_ID, $"Settings web page: {message}", ex);

        return Answer_Error(HttpStatusCode.InternalServerError, message);
    }

    /// <summary>
    /// THE WRITER REFUSED THE FILE, SO NOTHING WAS WRITTEN (plan 04 Task 2b, ruling P33): config.json is present
    /// and could not be read, or does not parse, and the writer left it byte for byte rather than replace it with
    /// the edited keys alone. The page hears it as the same 500 a write that threw is — to the owner both are
    /// "your edit did not happen" — carrying the writer's own reason (in use: try again; does not parse: fix it by
    /// hand). "Nothing in this request" is literal: one refused read refuses every accepted edit in the body, and
    /// the writer writes a body in one rename or not at all. NOT LOGGED HERE: the writer has already logged its
    /// one warning line for this refusal, and a second line from the page would count one event twice. Null
    /// when no result is WriteFailed.
    /// </summary>
    static (int Status, string ContentType, string Body)? Refuse_WriteFailed_OrNull(
        IReadOnlyList<(string Path, SettingsWriteOutcomes Outcome, string? Message_OrNull)> results)
    {
        foreach (var (_, outcome, message) in results)
        {
            if (outcome == SettingsWriteOutcomes.WriteFailed)
                return Answer_Error(HttpStatusCode.InternalServerError, Describe_WriteRefused(message));
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------
    // The request target
    // ---------------------------------------------------------------------------------------

    static (string Route, string Query) Split_Target(string target)
    {
        var queryStart = target.IndexOf('?');

        return queryStart < 0 ? (target, string.Empty) : (target[..queryStart], target[(queryStart + 1)..]);
    }

    /// <summary>
    /// The first value of <paramref name="key"/> in a query string, decoded, or null when the key is absent.
    /// Form-encoding's '+' is a space, as the page's <c>URLSearchParams</c> writes it; a malformed escape is
    /// left as typed (<see cref="Uri.UnescapeDataString(string)"/> never throws), so it reaches the writer as an
    /// unknown path and is refused in the writer's words.
    /// </summary>
    static string? Read_QueryValue_OrNull(string query, string key)
    {
        foreach (var pair in query.Split('&'))
        {
            var equals = pair.IndexOf('=');
            var name = Unescape(equals < 0 ? pair : pair[..equals]);

            if (string.Equals(name, key, StringComparison.Ordinal))
                return equals < 0 ? string.Empty : Unescape(pair[(equals + 1)..]);
        }

        return null;
    }

    static string Unescape(string text)
    {
        return Uri.UnescapeDataString(text.Replace('+', ' '));
    }

    // ---------------------------------------------------------------------------------------
    // Answers
    // ---------------------------------------------------------------------------------------

    static (int Status, string ContentType, string Body) Answer(HttpStatusCode status, JsonObject body)
    {
        // CLIENT TEXT IS ECHOED AS IT CAME (a route, a method, a Host, a DELETE's path): the default encoder writes
        // a lone surrogate as U+FFFD rather than throwing — measured 2026-09-23, and pinned by
        // AClientsText_WithALoneSurrogate_IsAnswered_NeverThrown, so a change of encoder here would go red there.
        // Indented: the page does not care, and the owner's brother reading it with curl through the tunnel does.
        return ((int)status, CONTENT_TYPE, body.ToJsonString(JsonWriting.INDENTED));
    }

    static (int Status, string ContentType, string Body) Answer_Error(HttpStatusCode status, string message)
    {
        return Answer(status, new JsonObject { ["error"] = message });
    }

    static string Describe_NotLoopback(string? host)
    {
        var named = string.IsNullOrWhiteSpace(host) ? "no Host header" : $"Host '{host}'";

        return $"This page answers only requests addressed to {string.Join(", ", LOOPBACK_HOST_NAMES)} (with any port) — "
            + $"this one carried {named}. From another machine, reach it through an SSH tunnel to the loopback address.";
    }

    static string Describe_Fenced(IReadOnlyList<string> fencedPaths)
    {
        return $"{string.Join(", ", fencedPaths.Select(path => $"'{path}'"))} cannot be changed from this page while web.token is empty: "
            + "set web.token first, or change this at the desktop / in config.json. Nothing in this request was applied.";
    }

    static string Describe_NotFound(string route)
    {
        return $"Nothing is served at '{route}' — the settings API is {SETTINGS_PATH}.";
    }

    static string Describe_MethodNotAllowed(string method)
    {
        return $"{method} is not supported on {SETTINGS_PATH} — allowed: {ALLOWED_METHODS}.";
    }

    static string Describe_Unauthorised()
    {
        return $"Editing needs web.token: send it in the {TOKEN_HEADER} header. Reading needs nothing.";
    }

    static string Describe_NotJson(string reason)
    {
        return $"The body is not JSON this API reads ({reason}). Send an object of setting paths to values.";
    }

    static string Describe_NotAnObject()
    {
        // No real path as the example: a renamed row would leave this sentence naming a setting that no longer exists.
        return "The body must be a JSON object of setting paths to values: {\"<path>\": <value>, …}.";
    }

    static string Describe_NoEdits()
    {
        return "The body names no setting — send at least one path and its value. To reset a setting, "
            + $"DELETE {SETTINGS_PATH}?{RESET_QUERY_KEY}=<path>.";
    }

    static string Describe_ResetNeedsAPath()
    {
        return $"DELETE {SETTINGS_PATH} resets one setting and needs it named: {SETTINGS_PATH}?{RESET_QUERY_KEY}=<path>.";
    }

    static string Describe_WriteFailed(string reason)
    {
        return $"config.json could not be written ({reason}) — nothing in this request was applied.";
    }

    static string Describe_WriteRefused(string? writersReason)
    {
        return $"{writersReason ?? "config.json was not written."} Nothing in this request was applied.";
    }
}
