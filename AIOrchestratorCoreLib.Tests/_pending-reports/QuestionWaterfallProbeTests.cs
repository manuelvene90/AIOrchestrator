using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;
using Xunit.Abstractions;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// NINE QUESTIONS IN FIVE MINUTES, WITH NO ANSWER IN BETWEEN.
///
/// The owner, 2026-09-09: *"I have just received like 10 questions in a row, without the session
/// waiting for my answers to each question before sending the next. This was a mess"*.
///
/// <para>
/// <see cref="AIOrchestratorCoreLib.Bridge.QuestionHold_Policy"/> pins the RULE as a predicate and
/// <c>QuestionHoldPolicyTests</c> pins its four answers. Neither can see the thing that actually
/// broke: the rule already existed in two other places and BOTH were unable to hold it — a
/// role-command sentence, and a PreToolUse hook that covered supervisors only and describes itself
/// as advisory. What has to be true is not "the predicate returns true", it is "the second question
/// DOES NOT REACH THE PHONE", and that is only observable by driving the engine end to end and
/// reading what the Telegram client was handed. So this probe asserts on the CONTENT of the sends —
/// one nonsense sentinel per question — never on a message count, which cannot tell a held question
/// from a dropped one from the same question retried.
/// </para>
/// <para>
/// THE THIRD FACT WAS RED WHEN IT WAS WRITTEN, and it is the owner's literal case — which is the
/// whole reason this file was worth its cost. The hold was evaluated once per APPEND, and an append
/// is one channel's worth of a single poll: the tailer packs every entry it read into it. Questions
/// written inside one 2-second tick (the owner's report: six of the nine were written inside the
/// same 20-millisecond batch) therefore arrived as ONE append, the first entry's send raised the
/// awaiting-answer flag, and the second entry of that same append was never re-tested against it.
/// The hold now sits INSIDE the entry loop, and the append can be delivered in part. See
/// <see cref="ASameTickBurst_IsHeldToo_BecauseTheHoldIsPerEntryNotPerAppend"/>.
/// </para>
/// </summary>
public class QuestionWaterfallProbeTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4242;

    const string DISPLAY_NAME = "IS · three decisions";

    /// <summary>
    /// One nonsense word per question, carried ONLY in the `QUESTION:` line. Deliberately NOT in the
    /// subject: the topic status line is built from the last subject and re-sends itself as the
    /// orchestration's pinned line, so a sentinel in a subject would read as "sent" without any
    /// question having been texted, and the negative assertions would fail for a reason that is not
    /// the defect.
    /// </summary>
    const string FIRST = "ALPHAKEY";
    const string SECOND = "BRAVOKEY";
    const string THIRD = "CHARLIEKEY";

    /// <summary>
    /// How long the owner is left deliberately silent before a "this did not arrive" assertion. Five
    /// mirror ticks, and well past the tailer's own two-quiet-poll window for releasing a trailing
    /// entry — so the silence cannot be the entry merely not having been READ yet. The release
    /// assertion that follows each of these closes that door from the other side: the entry was
    /// sitting there, and the owner's word is what let it out.
    /// </summary>
    const int SILENT_WINDOW_MS = 12_000;

    /// <summary>Free of any sentinel, and not a control word (bare "wait" / "go" would be).</summary>
    const string FIRST_ANSWER = "understood, take the shorter route";
    const string SECOND_ANSWER = "understood, take the other one";

    /// <summary>Timeline milestones — stamped into the recorder, never sent to anything.</summary>
    const string ANSWERED_ONE = "the owner answered the first";
    const string ANSWERED_TWO = "the owner answered the second";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly WaterfallTelegram_Fake _telegram;
    readonly RecordingLog_Fake _log;
    readonly ITestOutputHelper _out;

    public QuestionWaterfallProbeTests(ITestOutputHelper output)
    {
        _out = output;

        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-waterfall-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID},\"telegramItalianLayer\":false}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _log = new RecordingLog_Fake();
        _telegram = new WaterfallTelegram_Fake();

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, _launcher, _log, _telegram);
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
    }

    /// <summary>
    /// THE OWNER'S RULE: one question is with them at a time, and each further one is released by an
    /// ANSWER and by nothing else.
    ///
    /// <para>
    /// Both negative assertions discriminate on their own. Remove the hold and the second question
    /// goes out on the next 2-second tick — the tailer has already read it, and nothing else in the
    /// mirror path looks at whether the owner owes an answer. The second one also pins the RE-ARM:
    /// answering does not open a floodgate, it lets exactly one more through, because that one's own
    /// send raises the flag again.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AQuestionBurst_ReachesTheOwnerOneAtATime()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();

        Append_SupervisorEntry(orchId, 1, "decision one", Question_Body(FIRST));

        Assert.True(
            await Run_Until_Async(() => Was_Texted(FIRST), 30_000),
            "THE FIRST QUESTION NEVER ARRIVED, so nothing below proves anything about the hold — the "
            + $"harness reached no question at all.{Environment.NewLine}{Dump()}");

        // The session asks again while the first is still with the owner. This is the waterfall.
        Append_SupervisorEntry(orchId, 2, "decision two", Question_Body(SECOND));

        await Run_For_Async(SILENT_WINDOW_MS);

        Dump_ToOutput("second question written, owner has not answered");

        Assert.False(
            Was_Texted(SECOND),
            "THE WATERFALL: a second question reached the owner while the first was still unanswered."
            + $"{Environment.NewLine}{Dump()}");

        // HELD, NOT DROPPED — checked BEFORE the release, so a "hold" that works by throwing the
        // entry away could not pass the release assertion below by accident.
        Assert.True(
            Owner_ChannelContains(orchId, SECOND),
            "the held question is no longer in the channel file, so the silence above was silence "
            + $"about something that had been destroyed.{Environment.NewLine}{Dump()}");

        // The owner answers. Any word from them clears the awaiting-answer flag, so the held entry
        // is released on the following poll.
        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson(FIRST_ANSWER, updateId: 1001, messageId: 77));

        Assert.True(
            await Run_Until_Async(() => Was_Texted(SECOND), 30_000),
            "THE HELD QUESTION NEVER CAME: the owner answered and the second question did not follow, "
            + $"so the hold is a drop.{Environment.NewLine}{Dump()}");

        // ...and the second question's own send re-raises the flag, so a third waits exactly as the
        // second did. This is what separates "one at a time" from "one burst per answer".
        Append_SupervisorEntry(orchId, 3, "decision three", Question_Body(THIRD));

        await Run_For_Async(SILENT_WINDOW_MS);

        Dump_ToOutput("third question written, second still unanswered");

        Assert.False(
            Was_Texted(THIRD),
            "THE FLAG DID NOT RE-ARM: the owner answered once and everything queued behind it poured "
            + $"through, so answering opens a floodgate rather than admitting one question."
            + $"{Environment.NewLine}{Dump()}");

        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson(SECOND_ANSWER, updateId: 1002, messageId: 78));

        Assert.True(
            await Run_Until_Async(() => Was_Texted(THIRD), 30_000),
            "THE LAST QUESTION NEVER CAME: two answers were given and the third question is still "
            + $"nowhere.{Environment.NewLine}{Dump()}");

        Dump_ToOutput("all three delivered");
    }

    /// <summary>
    /// NOTHING IS LOST, NOTHING IS REORDERED, AND NOTHING ARRIVES TWICE.
    ///
    /// <para>
    /// The hold works by declining to CONFIRM the tailer's cursor, which is the same machinery that
    /// makes the mirror at-least-once: a held entry is re-emitted on every single poll until it is
    /// finally sent. That buys "never dropped" at the risk of "sent on every poll", so the exactly-once
    /// count below is not decoration — it is the other half of the mechanism. The order is read off
    /// the sends themselves rather than off a counter, because a count cannot tell a held question
    /// from the same question re-asked.
    /// </para>
    /// <para>
    /// The owner's two answers are stamped into the same timeline as the sends, so the assertion is
    /// the whole story in one sequence — question, answer, question, answer, question. Without that
    /// interleaving this fact would pass with the hold REMOVED, and it was verified that it does:
    /// three questions fired in a row still arrive once each, and still in the order they were
    /// written. Nothing-is-lost is a companion property, not a hold detector, until it is pinned to
    /// WHEN the owner spoke.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task NothingIsLost_TheHeldEntriesKeepTheirOrder()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();

        Append_SupervisorEntry(orchId, 1, "decision one", Question_Body(FIRST));

        Assert.True(
            await Run_Until_Async(() => Was_Texted(FIRST), 30_000),
            $"the first question never arrived, so this test reached nothing.{Environment.NewLine}{Dump()}");

        Append_SupervisorEntry(orchId, 2, "decision two", Question_Body(SECOND));

        // Long enough that a hold which re-sends on every poll would show up as duplicates below.
        await Run_For_Async(SILENT_WINDOW_MS);

        _telegram.Mark(ANSWERED_ONE);
        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson(FIRST_ANSWER, updateId: 2001, messageId: 87));

        Assert.True(
            await Run_Until_Async(() => Was_Texted(SECOND), 30_000),
            $"the second question never followed the first answer.{Environment.NewLine}{Dump()}");

        Append_SupervisorEntry(orchId, 3, "decision three", Question_Body(THIRD));

        await Run_For_Async(SILENT_WINDOW_MS);

        _telegram.Mark(ANSWERED_TWO);
        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson(SECOND_ANSWER, updateId: 2002, messageId: 88));

        Assert.True(
            await Run_Until_Async(() => Was_Texted(THIRD), 30_000),
            $"the third question never followed the second answer.{Environment.NewLine}{Dump()}");

        Dump_ToOutput("all three delivered");

        // THE WHOLE STORY IN ONE SEQUENCE: question, answer, question, answer, question. The
        // milestones are stamped into the recorder at the moment the owner's message is queued, so a
        // question that jumped its turn lands on the wrong side of one and this fails naming it.
        Assert.Equal(
            new[] { FIRST, ANSWERED_ONE, SECOND, ANSWERED_TWO, THIRD },
            Timeline_Milestones());

        foreach (var sentinel in new[] { FIRST, SECOND, THIRD })
        {
            Assert.True(
                Send_Count(sentinel) == 1,
                $"'{sentinel}' was texted {Send_Count(sentinel)} times. A held entry is re-emitted by "
                + "the tailer on every poll, so a hold that sends before it decides — or one that "
                + "settles the cursor after holding — turns one question into a repeating "
                + $"notification.{Environment.NewLine}{Dump()}");
        }

        // Everything is still on disk, in the order it was written: the phone was made to wait, the
        // record was not edited.
        var channel = File.ReadAllText(_paths.Get_OwnerChannelFile(orchId));

        Assert.True(
            channel.IndexOf(FIRST, StringComparison.Ordinal) >= 0
            && channel.IndexOf(FIRST, StringComparison.Ordinal) < channel.IndexOf(SECOND, StringComparison.Ordinal)
            && channel.IndexOf(SECOND, StringComparison.Ordinal) < channel.IndexOf(THIRD, StringComparison.Ordinal),
            $"the channel file no longer carries all three questions in the order they were written.{Environment.NewLine}{channel}");
    }

    /// <summary>
    /// THE OWNER'S LITERAL CASE, AND THE GAP THAT IS STILL OPEN (probe written 2026-09-09; expected
    /// to FAIL against the hold as it stands).
    ///
    /// <para>
    /// Three questions written back to back, with no mirror tick in between — the shape the owner
    /// reported, where six of the nine entries were written inside the same 20-millisecond batch.
    /// The tailer packs everything one poll read into a SINGLE <c>ICompletedChannelAppend</c> per
    /// channel. THIS TEST WAS RED WHEN IT WAS WRITTEN, and that is why it exists: the hold was
    /// evaluated once per append, before <c>Mirror_Append_Async</c> looped over that append's
    /// entries, so entry #1's send raised the awaiting-answer flag and entry #2 — already inside the
    /// loop — was never re-tested against it. Two questions on the phone, no answer in between, from
    /// a fix written specifically to prevent that.
    /// </para>
    /// <para>
    /// It is green because the hold moved INSIDE the entry loop and the flag is re-read before every
    /// entry. That forced the other half: an append can now be delivered in part, while the tailer
    /// confirms whole appends only, so the mirror remembers how many entries of a held append already
    /// went out and skips them when the tailer re-emits it. The exactly-once assertion in
    /// <see cref="NothingIsLost_TheHeldEntriesKeepTheirOrder"/> is what guards that half — get the
    /// skipping wrong and the owner is texted the same question on every poll.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ASameTickBurst_IsHeldToo_BecauseTheHoldIsPerEntryNotPerAppend()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();

        Append_SupervisorEntry(orchId, 1, "decision one", Question_Body(FIRST));
        Append_SupervisorEntry(orchId, 2, "decision two", Question_Body(SECOND));
        Append_SupervisorEntry(orchId, 3, "decision three", Question_Body(THIRD));

        Assert.True(
            await Run_Until_Async(() => Was_Texted(FIRST), 30_000),
            $"the first question never arrived, so this test reached nothing.{Environment.NewLine}{Dump()}");

        await Run_For_Async(SILENT_WINDOW_MS);

        Dump_ToOutput("burst written inside one tick, owner has not answered");

        Assert.False(
            Was_Texted(SECOND),
            "THE WATERFALL, INSIDE ONE TICK: two questions written in the same batch arrived in the "
            + "same append, and the hold — which is evaluated once per append — let the second one "
            + $"past after the first had already raised the flag.{Environment.NewLine}{Dump()}");
    }

    /// <summary>
    /// Carries the markers, so the mirror turns it into a button message, and the sentinel rides in
    /// the `QUESTION:` line — which is what <c>QuestionPrompt_Builder</c> puts above the buttons.
    /// The option labels are short on purpose: long ones are moved off the buttons and into the
    /// message body, which would change what "sent" means here for no gain.
    /// </summary>
    static string Question_Body(string sentinel)
    {
        return $"QUESTION: Which branch carries the {sentinel} change?\nOPTION: Keep\nOPTION: Replace";
    }

    /// <summary>Anything the bot was asked to SEND (not edits) that carries this question's sentinel.</summary>
    bool Was_Texted(string sentinel)
    {
        return Send_Count(sentinel) > 0;
    }

    int Send_Count(string sentinel)
    {
        var count = 0;

        foreach (var text in _telegram.SentTexts())
        {
            if (text.Contains(sentinel, StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    /// <summary>The sentinels in the order the bot was asked to send them, ignoring everything else.</summary>
    IReadOnlyList<string> Sentinel_ArrivalOrder()
    {
        List<string> order = [];

        foreach (var text in _telegram.SentTexts())
        {
            foreach (var sentinel in new[] { FIRST, SECOND, THIRD })
            {
                if (text.Contains(sentinel, StringComparison.Ordinal) && !order.Contains(sentinel))
                    order.Add(sentinel);
            }
        }

        return order;
    }

    /// <summary>
    /// The sentinels and the test's own milestones, interleaved in the order they happened. A
    /// sentinel counts once — repetition is the exactly-once assertion's business, not this one's.
    /// </summary>
    IReadOnlyList<string> Timeline_Milestones()
    {
        List<string> milestones = [];

        foreach (var moment in _telegram.Timeline())
        {
            if (moment.StartsWith(WaterfallTelegram_Fake.MARK_PREFIX, StringComparison.Ordinal))
            {
                milestones.Add(moment[WaterfallTelegram_Fake.MARK_PREFIX.Length..]);
                continue;
            }

            foreach (var sentinel in new[] { FIRST, SECOND, THIRD })
            {
                if (moment.Contains(sentinel, StringComparison.Ordinal) && !milestones.Contains(sentinel))
                    milestones.Add(sentinel);
            }
        }

        return milestones;
    }

    bool Owner_ChannelContains(string orchId, string fragment)
    {
        var file = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(file))
            return false;

        try
        {
            return File.ReadAllText(file).Contains(fragment, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    string Dump()
    {
        return $"sent: {_telegram.Dump_SentTexts()}{Environment.NewLine}"
            + $"buttons: {_telegram.Dump_ButtonTexts()}{Environment.NewLine}"
            + $"edits: {_telegram.Dump_EditedTexts()}{Environment.NewLine}"
            + $"topic names: {_telegram.Dump_TopicNames()}{Environment.NewLine}"
            + _log.Dump();
    }

    void Dump_ToOutput(string moment)
    {
        _out.WriteLine($"=== {moment} ===");
        _out.WriteLine($"sentinels seen, in order: {string.Join(" -> ", Sentinel_ArrivalOrder())}");
        _out.WriteLine($"timeline: {string.Join(" -> ", Timeline_Milestones())}");
        _out.WriteLine(Dump());
    }

    void Seed_OwnerChannel(string orchId)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");
    }

    /// <summary>
    /// The tailer registers a file it has never seen at its CURRENT END, so an entry appended before
    /// the first poll is behind the starting offset and never mirrors — a setup mistake that looks
    /// exactly like the defect under test. One short run baselines the file first.
    /// </summary>
    async Task<string> Start_WithChannelAlreadySeen_Async()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);

        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        _store.Set_DisplayName(session.OrchId, DISPLAY_NAME);
        Seed_OwnerChannel(session.OrchId);

        await Run_For_Async(4_000);

        return session.OrchId;
    }

    void Append_SupervisorEntry(string orchId, int index, string subject, string body)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(channelFile, $"\n## [{index}] FROM supervisor — {stamp} — {subject}\n{body}\n");
    }

    static string Build_OwnerMessageJson(string text, long updateId, long messageId)
    {
        return $"{{\"ok\":true,\"result\":[{{\"update_id\":{updateId},\"message\":{{\"message_id\":{messageId},"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
    }

    async Task Run_For_Async(int milliseconds)
    {
        await Run_Until_Async(() => false, milliseconds);
    }

    async Task<bool> Run_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);
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

/// <summary>
/// Records the TEXT of everything the bot was asked to send, because the subject of this probe is
/// WHICH QUESTION reached the phone and a message count cannot answer that — two of them could be
/// the same question retried, or the third one arriving early.
///
/// <para>
/// Sends and EDITS are kept apart on purpose: closing an answered question rewrites that question's
/// own message, so counting an edit as a send would report a question as "reaching the owner" on the
/// strength of it being closed, and would break the exactly-once count.
/// </para>
/// </summary>
internal sealed class WaterfallTelegram_Fake : ITelegramApiClient
{
    const string EMPTY_UPDATES = "{\"ok\":true,\"result\":[]}";

    /// <summary>Distinguishes a milestone stamped by the test from a text the bot was handed.</summary>
    public const string MARK_PREFIX = "«mark» ";

    readonly object _lock = new();
    readonly List<string> _sentTexts = [];

    /// <summary>
    /// Sends AND the test's own milestones, in one list, so "the second question arrived AFTER the
    /// owner answered the first" is a readable sequence rather than two timestamps compared by hand.
    /// </summary>
    readonly List<string> _timeline = [];
    readonly List<string> _buttonTexts = [];
    readonly List<string> _editedTexts = [];
    readonly List<string> _topicNames = [];
    string? _queuedUpdatesJson;
    long _nextMessageId = 9000;

    public void Queue_OwnerMessage(string updatesJson)
    {
        lock (_lock)
            _queuedUpdatesJson = updatesJson;
    }

    /// <summary>Stamps a milestone into the timeline — nothing is sent, nothing is counted as sent.</summary>
    public void Mark(string label)
    {
        lock (_lock)
            _timeline.Add(MARK_PREFIX + label);
    }

    public IReadOnlyList<string> SentTexts()
    {
        lock (_lock)
            return [.. _sentTexts];
    }

    public IReadOnlyList<string> Timeline()
    {
        lock (_lock)
            return [.. _timeline];
    }

    public string Dump_SentTexts()
    {
        lock (_lock)
            return string.Join(" | ", _sentTexts);
    }

    public string Dump_ButtonTexts()
    {
        lock (_lock)
            return string.Join(" | ", _buttonTexts);
    }

    public string Dump_EditedTexts()
    {
        lock (_lock)
            return string.Join(" | ", _editedTexts);
    }

    public string Dump_TopicNames()
    {
        lock (_lock)
            return string.Join(" | ", _topicNames);
    }

    public Task<long?> Send_Message_Async(long? messageThreadId, string text, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _sentTexts.Add(text);
            _timeline.Add(text);

            return Task.FromResult<long?>(_nextMessageId++);
        }
    }

    public Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, CancellationToken cancellationToken)
    {
        return Send_Message_Async(messageThreadId, html, cancellationToken);
    }

    public Task<long?> Send_MessageWithButtons_Async(
        long? messageThreadId,
        string text,
        IReadOnlyList<(string Data, string Label)> buttons,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _sentTexts.Add(text);
            _timeline.Add(text);
            _buttonTexts.Add(text);

            return Task.FromResult<long?>(_nextMessageId++);
        }
    }

    public Task<long?> Send_MessageWithButtonRows_Async(long? messageThreadId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken)
    {
        return Send_Message_Async(messageThreadId, text, cancellationToken);
    }

    public Task<long?> Send_MessageWithReplyKeyboard_Async(
        long? messageThreadId,
        string text,
        IReadOnlyList<IReadOnlyList<string>> keyboardRows,
        CancellationToken cancellationToken)
    {
        return Send_Message_Async(messageThreadId, text, cancellationToken);
    }

    public Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken)
    {
        lock (_lock)
            _editedTexts.Add(text);

        return Task.CompletedTask;
    }

    public Task Edit_MessageTextWithButtons_Async(long messageId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken)
    {
        return Edit_MessageText_Async(messageId, text, cancellationToken);
    }

    public Task Edit_MessageTextWithButtonRows_Async(long messageId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken)
    {
        return Edit_MessageText_Async(messageId, text, cancellationToken);
    }

    public async Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        string? queued;

        lock (_lock)
        {
            queued = _queuedUpdatesJson;
            _queuedUpdatesJson = null;
        }

        if (queued != null)
            return queued;

        // Stands in for the long poll. Without it this loop spins hot for the whole run.
        await Task.Delay(50, cancellationToken);

        return EMPTY_UPDATES;
    }

    public Task<long> Create_ForumTopic_Async(string topicName, CancellationToken cancellationToken)
    {
        return Task.FromResult(7777L);
    }

    public Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken)
    {
        lock (_lock)
            _topicNames.Add(newName);

        return Task.CompletedTask;
    }

    public Task Edit_GeneralForumTopic_Async(string newName, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Remove_TopicCreationPin_Async(long messageThreadId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Answer_CallbackQuery_Async(string callbackQueryId, string text, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Remove_MessageButtons_Async(long messageId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Delete_Message_Async(long messageId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Send_Photo_Async(long? messageThreadId, string filePath, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task Set_ChatMenuButton_ToCommands_Async(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken) => Task.FromResult(Array.Empty<byte>());
}
