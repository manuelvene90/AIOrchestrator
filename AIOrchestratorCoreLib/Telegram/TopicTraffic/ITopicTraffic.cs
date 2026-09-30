namespace AIOrchestratorCoreLib.Telegram.TopicTraffic;

/// <summary>
/// WHAT HAS LANDED IN EACH TOPIC SINCE THE APP STARTED — the two facts the status line's move is decided
/// on (owner, 2026-09-30): the newest message in the topic (anything newer than PULSE buries it), and
/// when the SESSION last put a message there (the move waits until that is a minute old). Key: the
/// topic's thread id, null for General.
///
/// <para>
/// WHY IT LEFT THE ENGINE. The newest-message map lived in <c>BridgeEngineModel</c> and was written
/// only by <c>Remember_TopicMessage</c>, which a dozen send sites never called — the alerts, the entry
/// documents, photos and attachments, the screenshots, the hold receipt, the busy narration, the
/// delivery receipts. Each of those buried PULSE while the app went on believing it was the last
/// message, so it was never moved. The fix is not a thirteenth call site: every send now reaches this
/// record through ONE chokepoint, the recording client
/// (<c>TelegramApiClient_Factory.Create_RecordingTopicTraffic</c>) that wraps the one Telegram client
/// the engine holds, so a send site added tomorrow buries PULSE without knowing this exists.
/// </para>
/// <para>
/// IN MEMORY ON PURPOSE, not in session.json: it is a fact about a conversation that is still
/// happening. What a restart loses is answered by <see cref="Find_Newest_OrAssumeBuried"/>.
/// </para>
/// </summary>
public interface ITopicTraffic
{
    /// <summary>A message with a known id is in the topic — sent by the app, or the owner's own. The HIGHEST id wins.</summary>
    void Note_Message(long? messageThreadId, long messageId);

    /// <summary>
    /// A message is in the topic but Telegram's reply did not hand its id back (<c>sendPhoto</c> and
    /// <c>sendDocument</c> return none through this client). It buries PULSE all the same — see the
    /// model for the lower bound that stands in for the id.
    /// </summary>
    void Note_UnidentifiedMessage(long? messageThreadId);

    /// <summary>
    /// The SESSION put a message in the topic at <paramref name="atUtc"/> — a mirrored entry of a
    /// supervisor, solo, communicator or member, with whatever it carried (pieces, document, photos,
    /// attachments, question card). The owner's messages and the app's own never come here: they bury
    /// PULSE without holding its move (owner, 2026-09-30: "not my last message").
    /// </summary>
    void Note_SessionMessage(long? messageThreadId, DateTime atUtc);

    /// <summary>
    /// The newest message known in the topic, for the burial question.
    ///
    /// <para>
    /// AFTER A RESTART nothing is known about any topic, and a line posted by the previous process may
    /// be twenty messages up — the owner's exact complaint — with nothing ever arriving to say so. So
    /// the FIRST time a topic with a line up (<paramref name="statusLineMessageId"/>) is asked about
    /// with no traffic recorded, the line is ASSUMED buried: the id just above it is recorded as the
    /// newest, which reposts it once at the first eligible tick. Every write there is silent, so a
    /// wrong assumption costs a delete and a send and rings nobody; a right one is the fix. The
    /// recorded value then resolves like any other: the fresh post's id is at least that high, and
    /// EQUAL is not buried.
    /// </para>
    /// Null when the topic has no line and no known traffic.
    /// </summary>
    TopicStatusLine_Planner.TopicNewestMessage? Find_Newest_OrAssumeBuried(long? messageThreadId, long? statusLineMessageId);

    /// <summary>When the session last put a message in the topic; null when it has put none since the app started.</summary>
    DateTime? Find_LastSessionMessageAtUtc_OrNull(long? messageThreadId);
}
