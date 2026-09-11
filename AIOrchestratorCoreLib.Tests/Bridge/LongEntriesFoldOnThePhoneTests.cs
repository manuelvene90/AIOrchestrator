using System.Text;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;
using AIOrchestratorCoreLib.Tests.TestSupport;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THREE LONG MESSAGES IN A ROW ARE HEAVY ON A PHONE — what the owner gets instead.
///
/// <para>
/// Stage 1i made a long supervisor entry ARRIVE, which was the defect it was written for. What it
/// arrives as is the subject here: several full-length messages, one after another, and the owner
/// scrolling through all of them before they can tell whether any of it needs them. Telegram's
/// collapsed quotation (<c>&lt;blockquote expandable&gt;</c>, Bot API 7.4) turns that into an opening
/// line plus a tap.
/// </para>
/// <para>
/// Driven end to end rather than poked, because the shape of the delivery has no getter — it is only
/// observable as the sequence of calls the Telegram client received, and the two facts that matter
/// most are negative ones: a SHORT entry must be untouched, and a folded one must still carry every
/// word.
/// </para>
/// </summary>
public class LongEntriesFoldOnThePhoneTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4444;

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly RecordingLog_Fake _log;
    readonly ByMethodTelegram_Fake _telegram;
    readonly IOrchestratorConfigProvider _configProvider;

    public LongEntriesFoldOnThePhoneTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-fold-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        Write_Config(telegramBlock: null);
        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _log = new RecordingLog_Fake();
        _telegram = new ByMethodTelegram_Fake();
        _configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, _configProvider, _store, new RecordingSpawner_Fake(), _log);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not delete is not a test result.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// THE REGRESSION GUARD, and the first thing to check on any change to this path: the owner reads
    /// short entries all day, and folding one would hide half a reply behind a "show more".
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AFourHundredCharacterEntry_ArrivesExACTLYASBEFORE_WithNoFold()
    {
        var body = "**READY**\n" + Build_Prose(6) + "\nShall I proceed?";

        Assert.InRange(body.Length, 350, 450);

        var engine = Build_Engine();
        var orchId = await Start_WithChannelAlreadySeen_Async(engine);

        Append_SupervisorEntry(orchId, 1, "ready", body);

        Assert.True(
            await Run_Until_Async(engine, () => _telegram.AnyHtmlSendContains("Shall I proceed?"), 20_000),
            $"the entry never reached Telegram.{Environment.NewLine}{_telegram.Dump_HtmlSends()}{Environment.NewLine}{_log.Dump()}");

        var sent = Assert.Single(_telegram.Html_Sends().Where(html => html.Contains("Shall I proceed?", StringComparison.Ordinal)));

        Assert.DoesNotContain("blockquote", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("(1/", sent, StringComparison.Ordinal);
        Assert.Contains("<b>READY</b>", sent, StringComparison.Ordinal);
        Assert.Empty(_telegram.Documents());
    }

    /// <summary>
    /// TWO THOUSAND CHARACTERS IS ONE MESSAGE AND ONE TAP. Under the 4096 cap, so nothing splits; over
    /// the fold threshold, so the opening is in the clear and the rest is behind the quotation.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ATwoThousandCharacterEntry_ArrivesAsOneMessage_OpeningInTheClearAndTheRestFolded()
    {
        var body = "**GOAL 2: V1 LIVE**\n" + Build_Prose(33) + "\nShall I proceed?";

        Assert.InRange(body.Length, 1_800, 2_200);

        var engine = Build_Engine();
        var orchId = await Start_WithChannelAlreadySeen_Async(engine);

        Append_SupervisorEntry(orchId, 1, "goal", body);

        Assert.True(
            await Run_Until_Async(engine, () => _telegram.AnyHtmlSendContains("<blockquote expandable>"), 20_000),
            "the entry did not arrive folded — a 2 000-character message is a screenful of scrolling "
                + $"the owner did not ask for.{Environment.NewLine}{_telegram.Dump_HtmlSends()}{Environment.NewLine}{_log.Dump()}");

        var sent = Assert.Single(_telegram.Html_Sends().Where(html => html.Contains("GOAL 2", StringComparison.Ordinal)));

        Assert.StartsWith("🔴", sent, StringComparison.Ordinal);
        Assert.Contains("<b>GOAL 2: V1 LIVE</b>", sent[..sent.IndexOf("<blockquote", StringComparison.Ordinal)], StringComparison.Ordinal);

        // ONE fold, never two: a nested blockquote is a 400, and a 400 here costs the owner a message.
        Assert.Equal(1, Count_Occurrences(sent, "<blockquote"));
        Assert.Contains("Shall I proceed?", sent, StringComparison.Ordinal);

        // A single message is under the attach threshold — the file is for entries the chat cannot hold.
        Assert.Empty(_telegram.Documents());
    }

    /// <summary>
    /// FIFTEEN THOUSAND CHARACTERS: folded, chunked, every line still there, and the whole entry ALSO
    /// attached once as the Markdown file it already is — because past a certain length reading it in
    /// the chat is the work.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AFifteenThousandCharacterEntry_IsFoldedAndChunkedAndAttachedOnce_WithNothingLost()
    {
        var lines = Enumerable.Range(1, 200)
            .Select(i => $"finding {i:D3} — the measurement, what it means, and what it costs to leave alone")
            .ToList();

        var body = "**THE SWEEP**\n" + string.Join('\n', lines) + "\nShall I proceed?";

        Assert.True(body.Length > 15_000, $"the fixture is only {body.Length} characters");

        var engine = Build_Engine();
        var orchId = await Start_WithChannelAlreadySeen_Async(engine);

        Append_SupervisorEntry(orchId, 1, "the sweep", body);

        Assert.True(
            await Run_Until_Async(engine, () => Delivered(lines) && _telegram.Documents().Count > 0, 30_000),
            "THE ANSWER WAS LOST, TRUNCATED OR NEVER ATTACHED. Folding hides text behind a tap; it is "
                + $"never allowed to remove it.{Environment.NewLine}html sends ({_telegram.Html_Sends().Count})"
                + $"{Environment.NewLine}documents: {_telegram.Dump_Documents()}{Environment.NewLine}{_log.Dump()}");

        var sends = _telegram.Html_Sends().Where(html => html.Contains("finding ", StringComparison.Ordinal)).ToList();

        Assert.True(sends.Count >= 4, $"a {body.Length}-character entry became {sends.Count} message(s)");

        // EVERY PIECE IS A FOLD, and the ones after the first are nothing BUT a fold: their head line
        // is their own number, because repeating the subject would read as separate messages.
        foreach (var html in sends)
        {
            Assert.Contains("<blockquote expandable>", html, StringComparison.Ordinal);
            Assert.Equal(1, Count_Occurrences(html, "<blockquote"));
            Assert.True(html.Length <= 4096, $"a piece renders to {html.Length} characters — Telegram refuses it");
        }

        Assert.StartsWith("(2/", sends[1], StringComparison.Ordinal);

        // IN ORDER, on the content and not only on the markers.
        var arrivalOrder = lines.Select(line => sends.FindIndex(html => html.Contains(line, StringComparison.Ordinal))).ToList();

        Assert.DoesNotContain(-1, arrivalOrder);
        Assert.Equal(arrivalOrder.OrderBy(index => index).ToList(), arrivalOrder);

        // ONCE, and with the whole entry in it — the file is a convenience, never a second delivery.
        var document = Assert.Single(_telegram.Documents());

        Assert.Equal(TOPIC_ID, document.ThreadId);
        Assert.Equal("the-sweep.md", document.FileName);
        Assert.Contains("finding 200", Encoding.UTF8.GetString(document.Content), StringComparison.Ordinal);
        Assert.Contains("THE SWEEP", document.CaptionHtml, StringComparison.Ordinal);
        Assert.True(document.CaptionHtml.Length <= 1024, $"caption is {document.CaptionHtml.Length} characters");
    }

    /// <summary>
    /// THE OWNER'S OWN OFF SWITCHES. Both keys at 0 give back the delivery that predates this stage —
    /// which is what makes them safe to hand-edit and what makes a bad fold recoverable without a build.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task WithBothKeysAtZero_ALongEntryArrivesUnfoldedAndUnattached_ExactlyAsBefore()
    {
        Write_Config(telegramBlock: "\"telegram\":{\"foldLongEntriesAbove\":0,\"attachEntriesAbove\":0},");

        var lines = Enumerable.Range(1, 200)
            .Select(i => $"finding {i:D3} — the measurement, what it means, and what it costs to leave alone")
            .ToList();

        var body = "**THE SWEEP**\n" + string.Join('\n', lines) + "\nShall I proceed?";

        var engine = Build_Engine();
        var orchId = await Start_WithChannelAlreadySeen_Async(engine);

        Append_SupervisorEntry(orchId, 1, "the sweep", body);

        Assert.True(
            await Run_Until_Async(engine, () => Delivered(lines), 30_000),
            $"the entry never arrived.{Environment.NewLine}{_log.Dump()}");

        foreach (var html in _telegram.Html_Sends().Where(html => html.Contains("finding ", StringComparison.Ordinal)))
            Assert.DoesNotContain("blockquote", html, StringComparison.Ordinal);

        Assert.Empty(_telegram.Documents());
    }

    bool Delivered(IReadOnlyList<string> lines)
    {
        var sends = _telegram.Html_Sends();

        return lines.All(line => sends.Any(html => html.Contains(line, StringComparison.Ordinal)));
    }

    static int Count_Occurrences(string text, string fragment)
    {
        var count = 0;
        var position = 0;

        while (true)
        {
            var found = text.IndexOf(fragment, position, StringComparison.Ordinal);

            if (found < 0)
                return count;

            count++;
            position = found + fragment.Length;
        }
    }

    static string Build_Prose(int lines)
    {
        return string.Join('\n', Enumerable.Range(1, lines).Select(i => $"finding {i:D2} — what it means and what it costs to leave alone"));
    }

    IBridgeEngine Build_Engine()
    {
        return BridgeEngine_Factory.Create_WithTelegramClient(
            _paths, _configProvider, _store, _launcher, _log, _telegram, BridgeTestTiming.Fast());
    }

    void Write_Config(string? telegramBlock)
    {
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],{telegramBlock}\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}}}");
    }

    /// <summary>
    /// The tailer registers a file it has never seen at its CURRENT END, so an entry appended before
    /// the first poll is behind the starting offset and never mirrors — a setup mistake that looks
    /// exactly like the defect under test. One short run baselines the file first.
    /// </summary>
    async Task<string> Start_WithChannelAlreadySeen_Async(IBridgeEngine engine)
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);

        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        _store.Set_DisplayName(session.OrchId, "IS · fold");

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        await Run_Until_Async(engine, () => false, BridgeTestTiming.Window_ForTicks(3));

        _telegram.Forget_Everything();

        return session.OrchId;
    }

    void Append_SupervisorEntry(string orchId, int index, string subject, string body)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(channelFile, $"\n## [{index}] FROM supervisor — {stamp} — {subject}\n{body}\n");
    }

    static async Task<bool> Run_Until_Async(IBridgeEngine engine, Func<bool> condition, int maxMilliseconds)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = engine.Run_Async(cancellation.Token);
        var satisfied = false;

        for (var waited = 0; waited < maxMilliseconds; waited += 100)
        {
            if (condition())
            {
                satisfied = true;
                break;
            }

            await Task.Delay(100);
        }

        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way these loops end.
        }

        return satisfied || condition();
    }
}
