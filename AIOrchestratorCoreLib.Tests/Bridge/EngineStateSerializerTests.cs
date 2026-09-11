using AIOrchestratorCoreLib.Bridge.EngineState;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// Pins the on-disk shape of <see cref="EngineStateSnapshot"/>: what survives a round trip, what a
/// forward- or backward-incompatible file does to it, and the two places a hand-edited or corrupted
/// file must not be allowed to invent a dangerous state (a deadline-less button, a defaulted
/// high-risk question) rather than simply losing the record.
///
/// <para>
/// NOTE ON EQUALITY. <see cref="EngineStateSnapshot"/> is a record, but several of its members are
/// <c>List</c>/<c>Dictionary</c> instances — reference types with no value-equality override — so the
/// record's own generated <c>Equals</c> would compare two structurally-identical snapshots as
/// UNEQUAL whenever they are backed by different collection instances (which a round trip always
/// produces). Every comparison below is therefore field-by-field, using <c>Assert.Equal</c> directly
/// on each collection — xUnit's own equality walks an <c>IEnumerable</c> element-by-element
/// regardless of the collection type's own <c>Equals</c>, which is what makes that safe.
/// </para>
/// </summary>
public class EngineStateSerializerTests
{
    static readonly DateTime T0 = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

    static EngineStateSnapshot Build_FullSnapshot()
    {
        return new EngineStateSnapshot
        {
            OwnerAwaitingAnswer = ["orch-1", "orch-2"],
            NudgedAboutEntry = new Dictionary<string, string>
            {
                ["sup"] = "entry-7",
                ["imp-1"] = "entry-3",
            },
            ConsecutiveRespawns = new Dictionary<string, int>
            {
                ["sup"] = 0,
                ["imp-1"] = 2,
            },
            PendingButtons =
            [
                new PendingButtonRecord
                {
                    Data = "opt-a1b2c3d4e5f6:0",
                    ThreadId = 42,
                    OptionText = "Merge",
                    GroupId = 1001,
                    QuestionText = "Merge now or hold?",
                    ExpiresUtc = T0.AddMinutes(30),
                    IsHighRisk = false,
                },
                new PendingButtonRecord
                {
                    Data = "opt-a1b2c3d4e5f7:1",
                    ThreadId = null,
                    OptionText = "Deploy",
                    GroupId = 1002,
                    QuestionText = "Deploy to production?",
                    ExpiresUtc = T0.AddMinutes(45),
                    IsHighRisk = true,
                },
            ],
            OpenQuestions =
            [
                new OpenQuestionRecord
                {
                    MessageId = 555,
                    OrchId = "orch-1",
                    Text = "Merge now or hold?",
                    AskedUtc = T0,
                    ButtonGroupId = 1001,
                    DeadlineUtc = T0.AddHours(1),
                    DefaultOptionIndex = 0,
                    IsHighRisk = false,
                    ReminderSent = true,
                },
            ],
            PendingConfirmations =
            [
                new PendingConfirmationRecord
                {
                    Code = "482913",
                    ThreadId = 42,
                    MessageId = 556,
                    OrchId = "orch-1",
                    OptionText = "Deploy",
                    QuestionText = "Deploy to production?",
                    ExpiresUtc = T0.AddMinutes(10),
                },
            ],
            CloseConfirmations =
            [
                new CloseConfirmationRecord
                {
                    ParkedPath = "/sup/.requests/awaiting-owner/close-1-abc.json",
                    OrchId = "orch-1",
                    Kind = "Orchestration",
                    MemberId = null,
                    Requester = "supervisor of orch-1",
                    AskedUtc = T0,
                    ExpiresUtc = T0.AddHours(12),
                    PromptMessageId = 557,
                },
                new CloseConfirmationRecord
                {
                    ParkedPath = "/sup/.requests/awaiting-owner/close-imp-def.json",
                    OrchId = "orch-2",
                    Kind = "Implementer",
                    MemberId = "imp-1",
                    Requester = "the close-implementer request file",
                    AskedUtc = T0.AddMinutes(3),
                    ExpiresUtc = null,
                    PromptMessageId = null,
                },
            ],
            ButtonGroupSequence = 1002,
            DispatchPausedUntilUtc = T0.AddMinutes(15),
            DispatchPauseReason = "usage limit hit at 97%",
        };
    }

    static void AssertSnapshotsEqual(EngineStateSnapshot expected, EngineStateSnapshot actual)
    {
        Assert.Equal(expected.OwnerAwaitingAnswer, actual.OwnerAwaitingAnswer);
        Assert.Equal(expected.NudgedAboutEntry, actual.NudgedAboutEntry);
        Assert.Equal(expected.ConsecutiveRespawns, actual.ConsecutiveRespawns);
        Assert.Equal(expected.PendingButtons, actual.PendingButtons);
        Assert.Equal(expected.OpenQuestions, actual.OpenQuestions);
        Assert.Equal(expected.PendingConfirmations, actual.PendingConfirmations);
        Assert.Equal(expected.CloseConfirmations, actual.CloseConfirmations);
        Assert.Equal(expected.ButtonGroupSequence, actual.ButtonGroupSequence);
        Assert.Equal(expected.DispatchPausedUntilUtc, actual.DispatchPausedUntilUtc);
        Assert.Equal(expected.DispatchPauseReason, actual.DispatchPauseReason);
    }

    /// <summary>
    /// The central contract: a fully-populated snapshot — every collection non-empty, a paused
    /// dispatcher, a non-zero button group sequence — survives To_Json → Parse with every field
    /// equal and nothing dropped. Every DateTime here is built on a whole second on purpose: unix
    /// seconds is the wire format, so sub-second precision is deliberately NOT preserved, and an
    /// input with sub-second precision would make this assertion dishonest rather than proving the
    /// round trip actually works.
    /// </summary>
    [Fact]
    public void AFullSnapshot_RoundTrips_WithEveryFieldEqual_AndNothingDropped()
    {
        var original = Build_FullSnapshot();

        var json = EngineState_Serializer.To_Json(original);
        var (roundTripped, dropped) = EngineState_Serializer.Parse(json);

        Assert.Equal(0, dropped);
        AssertSnapshotsEqual(original, roundTripped);
    }

    /// <summary>
    /// A top-level field this build does not know about — the exact shape of a file written by a
    /// NEWER build — must be ignored rather than rejected. A rollback that took the bridge down over
    /// an unknown field would turn "ship a new field" into "ship a field you can never revert past".
    /// </summary>
    [Fact]
    public void AnUnknownTopLevelField_IsIgnored_NotRejected()
    {
        var json = """
        {
          "ownerAwaitingAnswer": ["orch-9"],
          "futureTopLevelFeature": { "anything": "goes here" },
          "nudgedAboutEntry": {},
          "pendingButtons": [],
          "openQuestions": [],
          "pendingConfirmations": [],
          "consecutiveRespawns": {},
          "buttonGroupSequence": 3,
          "dispatchPausedUntilUtc": null,
          "dispatchPauseReason": null
        }
        """;

        var (snapshot, dropped) = EngineState_Serializer.Parse(json);

        Assert.Equal(0, dropped);
        Assert.Equal(new List<string> { "orch-9" }, snapshot.OwnerAwaitingAnswer);
        Assert.Equal(3, snapshot.ButtonGroupSequence);
    }

    /// <summary>
    /// The same tolerance one level down: an unknown field INSIDE a record must not sink that
    /// record, for the same forward-compatibility reason.
    /// </summary>
    [Fact]
    public void AnUnknownFieldInsideARecord_IsIgnored_NotRejected()
    {
        var expiresUnix = new DateTimeOffset(T0.AddMinutes(30)).ToUnixTimeSeconds();

        var json = $$"""
        {
          "pendingButtons": [
            {
              "data": "opt-a1b2c3d4e5f6:0",
              "optionText": "Merge",
              "questionText": "Merge now or hold?",
              "expiresUtc": {{expiresUnix}},
              "futureFieldOnTheButton": "ignored"
            }
          ]
        }
        """;

        var (snapshot, dropped) = EngineState_Serializer.Parse(json);

        Assert.Equal(0, dropped);
        var button = Assert.Single(snapshot.PendingButtons);
        Assert.Equal("opt-a1b2c3d4e5f6:0", button.Data);
        Assert.Equal("Merge", button.OptionText);
    }

    /// <summary>
    /// Truncated, half-written, or simply not JSON are all one situation to this reader: it must
    /// never throw, and it must say something was lost rather than silently returning a fresh state
    /// that looks like a normal first run.
    /// </summary>
    [Fact]
    public void MalformedJson_ReturnsAnEmptySnapshot_WithDroppedGreaterThanZero_RatherThanThrowing()
    {
        var (snapshot, dropped) = EngineState_Serializer.Parse("{ this is not valid json ][");

        Assert.True(dropped > 0);
        Assert.Empty(snapshot.OwnerAwaitingAnswer);
        Assert.Empty(snapshot.PendingButtons);
        Assert.Empty(snapshot.OpenQuestions);
        Assert.Empty(snapshot.PendingConfirmations);
        Assert.Empty(snapshot.NudgedAboutEntry);
        Assert.Empty(snapshot.ConsecutiveRespawns);
        Assert.Equal(0, snapshot.ButtonGroupSequence);
        Assert.Null(snapshot.DispatchPausedUntilUtc);
    }

    /// <summary>
    /// A pending button's expiry cannot be defaulted in either direction: inventing an already-past
    /// expiry refuses every tap on a button that was actually still live, and inventing a
    /// never-expires default creates a button that lives forever. Dropping it is the only safe
    /// reading — and the other records in the same file, including sibling buttons, must survive
    /// that one drop rather than the whole file being discarded.
    /// </summary>
    [Fact]
    public void APendingButtonMissingItsExpiry_IsDropped_AndOtherRecordsInTheSameFileSurvive()
    {
        var goodExpiry = new DateTimeOffset(T0.AddMinutes(45)).ToUnixTimeSeconds();
        var askedUtc = new DateTimeOffset(T0).ToUnixTimeSeconds();
        var confirmationExpiry = new DateTimeOffset(T0.AddMinutes(10)).ToUnixTimeSeconds();

        var json = $$"""
        {
          "pendingButtons": [
            { "data": "opt-nodeadline:0", "optionText": "Yes", "questionText": "Deploy?" },
            { "data": "opt-hasdeadline:1", "optionText": "No", "questionText": "Deploy?", "expiresUtc": {{goodExpiry}} }
          ],
          "openQuestions": [
            { "messageId": 5, "orchId": "orch-1", "text": "Merge?", "askedUtc": {{askedUtc}} }
          ],
          "pendingConfirmations": [
            { "code": "111222", "orchId": "orch-1", "optionText": "Merge", "questionText": "Merge?", "expiresUtc": {{confirmationExpiry}} }
          ]
        }
        """;

        var (snapshot, dropped) = EngineState_Serializer.Parse(json);

        Assert.Equal(1, dropped);
        var survivingButton = Assert.Single(snapshot.PendingButtons);
        Assert.Equal("opt-hasdeadline:1", survivingButton.Data);
        Assert.Single(snapshot.OpenQuestions);
        Assert.Single(snapshot.PendingConfirmations);
    }

    /// <summary>
    /// The complementary guarantee, enforced on the READ side and not just when writing: a
    /// hand-edited (or buggy-build-written) file that carries BOTH isHighRisk true and a
    /// defaultOptionIndex must come back with the default STRIPPED, never with both. Honouring the
    /// default on a high-risk question would let a hand-edited file arm an unattended approval of a
    /// push — the one outcome this whole record shape exists to make impossible.
    /// </summary>
    [Fact]
    public void AHighRiskQuestionCarryingADefault_ComesBackWithTheDefaultStripped()
    {
        var askedUtc = new DateTimeOffset(T0).ToUnixTimeSeconds();

        var json = $$"""
        {
          "openQuestions": [
            {
              "messageId": 900,
              "orchId": "orch-1",
              "text": "Deploy to production?",
              "askedUtc": {{askedUtc}},
              "isHighRisk": true,
              "defaultOptionIndex": 1
            }
          ]
        }
        """;

        var (snapshot, dropped) = EngineState_Serializer.Parse(json);

        Assert.Equal(0, dropped);
        var question = Assert.Single(snapshot.OpenQuestions);
        Assert.True(question.IsHighRisk);
        Assert.Null(question.DefaultOptionIndex);
    }

    /// <summary>
    /// THE DROP RULE INVERTS FOR A CLOSE CONFIRMATION, and this pins the inversion rather than
    /// leaving it to a comment. Everywhere else a half-legible record is dropped, because a defaulted
    /// field is a decision taken by a bug. A close confirmation decides nothing: it is never restored
    /// into the live registry and no tap is ever matched against it, so the only harm it can do to a
    /// human is to VANISH from the file they are reading at 2 a.m. What is legible is kept; only the
    /// identity — which request, which orchestration — is required.
    /// </summary>
    [Fact]
    public void ACloseConfirmationMissingItsOptionalFields_IsKept_NotDropped()
    {
        var json = """
        {
          "closeConfirmations": [
            { "parkedPath": "/sup/.requests/awaiting-owner/close-1.json", "orchId": "orch-1" }
          ]
        }
        """;

        var (snapshot, dropped) = EngineState_Serializer.Parse(json);

        Assert.Equal(0, dropped);
        var record = Assert.Single(snapshot.CloseConfirmations);
        Assert.Equal("orch-1", record.OrchId);
        Assert.Equal("unrecorded", record.Kind);
        Assert.Null(record.ExpiresUtc);
        Assert.Null(record.PromptMessageId);
    }

    /// <summary>
    /// The floor under that leniency: a row that cannot say WHICH request it is about is not a record
    /// of anything, so it is dropped and counted like any other unreadable record.
    /// </summary>
    [Fact]
    public void ACloseConfirmationWithNoParkedPath_IsDropped_AndCounted()
    {
        var json = """
        {
          "closeConfirmations": [
            { "orchId": "orch-1", "kind": "Orchestration" },
            { "parkedPath": "/sup/.requests/awaiting-owner/close-2.json", "orchId": "orch-2" }
          ]
        }
        """;

        var (snapshot, dropped) = EngineState_Serializer.Parse(json);

        Assert.Equal(1, dropped);
        Assert.Equal("orch-2", Assert.Single(snapshot.CloseConfirmations).OrchId);
    }

    /// <summary>
    /// A kind this build has never heard of survives the round trip as TEXT. The field is written as
    /// the enum's name and read back as a string on purpose: a fourth kind added by a newer build must
    /// come back readable on a rollback rather than being normalised into one of the three we know,
    /// which would put a wrong noun in front of whoever is reading the file.
    /// </summary>
    [Fact]
    public void ACloseConfirmationOfAnUnknownKind_ComesBackUnchanged()
    {
        var json = """
        {
          "closeConfirmations": [
            { "parkedPath": "/sup/.requests/awaiting-owner/x.json", "orchId": "orch-9", "kind": "Rename" }
          ]
        }
        """;

        var (snapshot, dropped) = EngineState_Serializer.Parse(json);

        Assert.Equal(0, dropped);
        Assert.Equal("Rename", Assert.Single(snapshot.CloseConfirmations).Kind);
    }
}
