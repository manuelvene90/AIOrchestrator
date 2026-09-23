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
/// the header absent or wrong), 404 (not /settings), 405 (not GET/PUT/DELETE), 500 (config.json could not be
/// written — nothing in that request was applied).
/// </code>
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

    /// <summary>
    /// A REPEATED KEY IS REFUSED, NOT LAST-WINS. <c>{"x":30,"x":45}</c> is two answers to one question, and
    /// which the owner meant is not this class's to guess; refusing it at the parse is also what keeps a
    /// duplicate from surfacing later as a throw inside a lazily built <see cref="JsonObject"/>.
    /// </summary>
    static readonly JsonDocumentOptions PUT_BODY_OPTIONS = new() { AllowDuplicateProperties = false };

    /// <summary>
    /// One request, one answer, and never a throw for anything a client can send. <paramref name="path"/> is the
    /// request target as it arrived — the path and an optional query, still percent-encoded
    /// (<c>HttpListenerRequest.RawUrl</c>); <paramref name="header"/> looks a request header up by name.
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
        var (route, query) = Split_Target(path);

        if (!string.Equals(route, SETTINGS_PATH, StringComparison.Ordinal))
            return Answer_Error(HttpStatusCode.NotFound, Describe_NotFound(route));

        return method switch
        {
            GET_METHOD => Answer_Get(paths, configuredToken, log),
            PUT_METHOD => Refuse_Unauthorised_OrNull(header, configuredToken) ?? Answer_Put(body, paths, log),
            DELETE_METHOD => Refuse_Unauthorised_OrNull(header, configuredToken) ?? Answer_Delete(query, paths, log),

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

    static (int Status, string ContentType, string Body) Answer_Put(string body, ISupervisionPaths paths, IOrchestrationLog? log)
    {
        var (edits, problem) = Parse_PutBody(body);

        if (edits == null)
            return Answer_Error(HttpStatusCode.BadRequest, problem ?? Describe_NotAnObject());

        try
        {
            return Answer_Results(Settings_Writer.Apply_Many(paths, edits, log));
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
    /// </summary>
    static (IReadOnlyList<(string Path, JsonNode? Value)>? Edits_OrNull, string? Problem_OrNull) Parse_PutBody(string body)
    {
        JsonNode? root;

        try
        {
            root = JsonNode.Parse(body, nodeOptions: null, PUT_BODY_OPTIONS);
        }
        catch (JsonException ex)
        {
            return (null, Describe_NotJson(ex.Message));
        }

        if (root is not JsonObject edits)
            return (null, Describe_NotAnObject());

        if (edits.Count == 0)
            return (null, Describe_NoEdits());

        return (edits.Select(edit => (edit.Key, edit.Value)).ToArray(), null);
    }

    static (int Status, string ContentType, string Body) Answer_Delete(string query, ISupervisionPaths paths, IOrchestrationLog? log)
    {
        var settingPath = Read_QueryValue_OrNull(query, RESET_QUERY_KEY);

        if (string.IsNullOrWhiteSpace(settingPath))
            return Answer_Error(HttpStatusCode.BadRequest, Describe_ResetNeedsAPath());

        try
        {
            var (outcome, message) = Settings_Writer.Reset(paths, settingPath, log);

            return Answer_Results([(settingPath, outcome, message)]);
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
        // Indented: the page does not care, and the owner's brother reading it with curl through the tunnel does.
        return ((int)status, CONTENT_TYPE, body.ToJsonString(JsonWriting.INDENTED));
    }

    static (int Status, string ContentType, string Body) Answer_Error(HttpStatusCode status, string message)
    {
        return Answer(status, new JsonObject { ["error"] = message });
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
}
