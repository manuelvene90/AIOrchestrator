using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Configuration.PhoneSettings;

/// <summary>
/// WHAT REACHES THE OWNER'S PHONE AND HOW A TOPIC LOOKS — every <c>phone.*</c> and <c>topic.*</c> row
/// of the settings catalogue, resolved catalogue → preset → config.json and handed to the seams as
/// typed values (plan 03). A seam reads <c>_configProvider.Get_Current().Phone.&lt;X&gt;</c> at the point
/// of effect and never caches it, because the provider re-reads config.json on its write stamp.
///
/// <para>
/// THE BLOCKS ARE SPLIT BY PATH PREFIX, NOT BY CATALOGUE CATEGORY — deliberately. The catalogue files
/// <c>phone.receipts</c> and <c>pulse.holdToggle</c> together under <c>Receipts</c>, which is a MENU
/// section a renderer draws; a block is a JSON NEIGHBOURHOOD, the object an owner edits in config.json.
/// So <c>phone.*</c> and <c>topic.*</c> live here, and <c>pulse.*</c> and <c>general.buttons</c> live on
/// <c>IPulseSettings</c>. Regrouping these into three blocks by category would put
/// <c>phone.receipts</c> in a block that is not where config.json writes it, and move
/// <c>pulse.holdToggle</c> away from the bar it decides — breaking both halves at once.
/// </para>
/// <para>
/// THE TWO RE-HOMED PROSE ROWS ARE NOT HERE. <c>phone.foldLongEntriesAbove</c> and
/// <c>phone.attachEntriesAbove</c> share this prefix but were already read, through
/// <c>IOrchestratorConfig.TelegramProse</c>, before this block existed; they resolve through the same
/// catalogue there, and a second property for the same row would be a second copy of one fact.
/// </para>
/// </summary>
public interface IPhoneSettings
{
    /// <summary><c>phone.push</c> — which owner-channel entries are sent to the phone at all.</summary>
    PhonePushModes Push { get; }

    /// <summary><c>phone.status.periodic</c> — whether the app pushes an unprompted periodic status.</summary>
    bool PeriodicStatus { get; }

    /// <summary><c>phone.status.intervalMinutes</c> — minutes between periodic statuses, when they are on (5–120).</summary>
    int PeriodicStatusIntervalMinutes { get; }

    /// <summary>
    /// <c>phone.appMessagesRing</c> — whether the owner's session's NARRATION rings (D7 answer (b), 2026-09-14).
    /// False keeps a question, a BLOCKED, a file, the greeting and the answer ringing; read by
    /// <c>EntrySound_Resolver</c>.
    /// </summary>
    bool AppMessagesRing { get; }

    /// <summary><c>phone.replyKeyboard</c> — whether a bar of slash commands sits above the input box.</summary>
    ReplyKeyboardModes ReplyKeyboard { get; }

    /// <summary><c>phone.receipts</c> — how the app acknowledges an owner message.</summary>
    ReceiptStyles Receipts { get; }

    /// <summary><c>topic.onClose</c> — whether closing an orchestration deletes its topic or closes it.</summary>
    TopicCloseActions TopicOnClose { get; }

    /// <summary><c>topic.modeGlyphs</c> — whether the delivery-mode glyphs are drawn on the topic name or in PULSE's header.</summary>
    ModeGlyphPlacements TopicModeGlyphs { get; }

    /// <summary>
    /// <c>phone.aggregationSeconds</c> — how long an owner message waits in the buffer before it is handed
    /// to the session (1–60). Read by the engine's flush on every pass through
    /// <c>OwnerAggregationWindow_Resolver</c>, which is also where a test's custom timing outranks it.
    /// </summary>
    int AggregationSeconds { get; }

    /// <summary>
    /// <c>phone.finishedMessageSeconds</c> — the shorter wait a single finished message serves (0–60). AS
    /// RESOLVED, NOT AS SERVED: a value above the window is legal here and clamped to the window by the
    /// buffer at the point of use, so this property never pretends to know which window is in force.
    /// </summary>
    int FinishedMessageSeconds { get; }

    /// <summary>
    /// <c>topic.repoColours</c> — whether a new topic is created with its repository's colour (the fork's
    /// brief F1). Read by <c>RepoTopicColour_Resolver</c> at each topic creation; off assigns nothing and
    /// persists nothing. Telegram takes a colour only at creation, so it never repaints an existing topic.
    /// </summary>
    bool TopicRepoColours { get; }
}
