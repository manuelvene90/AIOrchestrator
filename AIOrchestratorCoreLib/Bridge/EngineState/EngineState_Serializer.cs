using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Bridge.EngineState;

/// <summary>
/// The on-disk shape of <see cref="EngineStateSnapshot"/>, as pure text↔state so the SHAPE is
/// testable without a filesystem — the same split <see cref="Limits.LimitAlertState_Store"/> makes,
/// and for the same reason: what carries the risk here is the reading, not the writing.
///
/// <para>
/// FAILS TOWARD THE VISIBLE SIDE, FIELD BY FIELD. A record that cannot be read is DROPPED rather
/// than defaulted into something plausible: a pending button with no expiry would otherwise be read
/// as one that expired at <c>default(DateTime)</c> — refusing every tap — or, worse if the default
/// went the other way, as one that never expires. Dropping it costs the owner a dead keyboard they
/// can answer by typing; inventing a field costs them a decision taken by a bug. The count of what
/// was dropped is returned so the caller can say so instead of the file silently shrinking.
/// </para>
/// <para>
/// UNKNOWN FIELDS ARE IGNORED, NOT REJECTED. A file written by a newer build must not take the
/// bridge down on a rollback, and the fields this stage adds are exactly the fields a next stage
/// will add more of.
/// </para>
/// </summary>
public static class EngineState_Serializer
{
    const string OWNER_AWAITING_ANSWER = "ownerAwaitingAnswer";
    const string NUDGED_ABOUT_ENTRY = "nudgedAboutEntry";
    const string PENDING_BUTTONS = "pendingButtons";
    const string OPEN_QUESTIONS = "openQuestions";
    const string PENDING_CONFIRMATIONS = "pendingConfirmations";
    const string CLOSE_CONFIRMATIONS = "closeConfirmations";
    const string CONSECUTIVE_RESPAWNS = "consecutiveRespawns";
    const string BUTTON_GROUP_SEQUENCE = "buttonGroupSequence";
    const string DISPATCH_PAUSED_UNTIL = "dispatchPausedUntilUtc";
    const string DISPATCH_PAUSE_REASON = "dispatchPauseReason";

    /// <summary>
    /// Reads a snapshot and says how many records it had to drop. Never throws for the CONTENT of
    /// the text: malformed JSON returns an empty snapshot with <c>Dropped</c> raised, and the caller
    /// decides whether that is a first run or a corruption worth quarantining.
    /// </summary>
    public static (EngineStateSnapshot Snapshot, int Dropped) Parse(string rawJson)
    {
        JsonObject? root;

        try
        {
            root = JsonNode.Parse(rawJson) as JsonObject;
        }
        catch
        {
            // Broad by intent: truncated, half-written, or not JSON at all are one situation here.
            return (EngineStateSnapshot.Empty, 1);
        }

        if (root == null)
            return (EngineStateSnapshot.Empty, 1);

        var dropped = 0;

        var snapshot = new EngineStateSnapshot
        {
            OwnerAwaitingAnswer = Read_StringList(root[OWNER_AWAITING_ANSWER]),
            NudgedAboutEntry = Read_StringMap(root[NUDGED_ABOUT_ENTRY]),
            ConsecutiveRespawns = Read_IntMap(root[CONSECUTIVE_RESPAWNS]),
            PendingButtons = Read_Records(root[PENDING_BUTTONS], Read_Button_OrNull, ref dropped),
            OpenQuestions = Read_Records(root[OPEN_QUESTIONS], Read_Question_OrNull, ref dropped),
            PendingConfirmations = Read_Records(root[PENDING_CONFIRMATIONS], Read_Confirmation_OrNull, ref dropped),
            CloseConfirmations = Read_Records(root[CLOSE_CONFIRMATIONS], Read_CloseConfirmation_OrNull, ref dropped),
            ButtonGroupSequence = Read_Long_OrNull(root[BUTTON_GROUP_SEQUENCE]) ?? 0,
            DispatchPausedUntilUtc = Read_Instant_OrNull(root[DISPATCH_PAUSED_UNTIL]),
            DispatchPauseReason = Read_String_OrNull(root[DISPATCH_PAUSE_REASON]),
        };

        return (snapshot, dropped);
    }

    public static string To_Json(EngineStateSnapshot snapshot)
    {
        var awaiting = new JsonArray();

        foreach (var orchId in snapshot.OwnerAwaitingAnswer)
            awaiting.Add(orchId);

        var nudged = new JsonObject();

        foreach (var pair in snapshot.NudgedAboutEntry)
            nudged[pair.Key] = pair.Value;

        var respawns = new JsonObject();

        foreach (var pair in snapshot.ConsecutiveRespawns)
            respawns[pair.Key] = pair.Value;

        var buttons = new JsonArray();

        foreach (var button in snapshot.PendingButtons)
        {
            buttons.Add(new JsonObject
            {
                ["data"] = button.Data,
                ["threadId"] = button.ThreadId,
                ["optionText"] = button.OptionText,
                ["groupId"] = button.GroupId,
                ["questionText"] = button.QuestionText,
                ["expiresUtc"] = Write_Instant(button.ExpiresUtc),
                ["isHighRisk"] = button.IsHighRisk,
                ["answersNothing"] = button.AnswersNothing,
            });
        }

        var questions = new JsonArray();

        foreach (var question in snapshot.OpenQuestions)
        {
            questions.Add(new JsonObject
            {
                ["messageId"] = question.MessageId,
                ["orchId"] = question.OrchId,
                ["text"] = question.Text,
                ["prompt"] = question.Prompt,
                ["askedUtc"] = Write_Instant(question.AskedUtc),
                ["buttonGroupId"] = question.ButtonGroupId,
                ["deadlineUtc"] = question.DeadlineUtc == null ? null : Write_Instant(question.DeadlineUtc.Value),
                ["defaultOptionIndex"] = question.DefaultOptionIndex,
                ["isHighRisk"] = question.IsHighRisk,
                ["reminderSent"] = question.ReminderSent,
            });
        }

        var confirmations = new JsonArray();

        foreach (var confirmation in snapshot.PendingConfirmations)
        {
            confirmations.Add(new JsonObject
            {
                ["code"] = confirmation.Code,
                ["threadId"] = confirmation.ThreadId,
                ["messageId"] = confirmation.MessageId,
                ["orchId"] = confirmation.OrchId,
                ["optionText"] = confirmation.OptionText,
                ["questionText"] = confirmation.QuestionText,
                ["expiresUtc"] = Write_Instant(confirmation.ExpiresUtc),
            });
        }

        var closeConfirmations = new JsonArray();

        foreach (var confirmation in snapshot.CloseConfirmations)
        {
            closeConfirmations.Add(new JsonObject
            {
                ["parkedPath"] = confirmation.ParkedPath,
                ["orchId"] = confirmation.OrchId,
                ["kind"] = confirmation.Kind,
                ["memberId"] = confirmation.MemberId,
                ["requester"] = confirmation.Requester,
                ["askedUtc"] = Write_Instant(confirmation.AskedUtc),
                ["expiresUtc"] = confirmation.ExpiresUtc == null ? null : Write_Instant(confirmation.ExpiresUtc.Value),
                ["promptMessageId"] = confirmation.PromptMessageId,
            });
        }

        var root = new JsonObject
        {
            [OWNER_AWAITING_ANSWER] = awaiting,
            [NUDGED_ABOUT_ENTRY] = nudged,
            [PENDING_BUTTONS] = buttons,
            [OPEN_QUESTIONS] = questions,
            [PENDING_CONFIRMATIONS] = confirmations,
            [CLOSE_CONFIRMATIONS] = closeConfirmations,
            [CONSECUTIVE_RESPAWNS] = respawns,
            [BUTTON_GROUP_SEQUENCE] = snapshot.ButtonGroupSequence,
            [DISPATCH_PAUSED_UNTIL] = snapshot.DispatchPausedUntilUtc == null ? null : Write_Instant(snapshot.DispatchPausedUntilUtc.Value),
            [DISPATCH_PAUSE_REASON] = snapshot.DispatchPauseReason,
        };

        return root.ToJsonString(Configuration.JsonWriting.INDENTED);
    }

    static IReadOnlyList<TRecord> Read_Records<TRecord>(JsonNode? node, Func<JsonObject, TRecord?> read, ref int dropped)
        where TRecord : class
    {
        List<TRecord> records = [];

        if (node is not JsonArray array)
            return records;

        foreach (var element in array)
        {
            if (element is not JsonObject entry)
            {
                dropped++;
                continue;
            }

            var record = read(entry);

            if (record == null)
                dropped++;
            else
                records.Add(record);
        }

        return records;
    }

    static PendingButtonRecord? Read_Button_OrNull(JsonObject entry)
    {
        var data = Read_String_OrNull(entry["data"]);
        var optionText = Read_String_OrNull(entry["optionText"]);
        var questionText = Read_String_OrNull(entry["questionText"]);
        var expiresUtc = Read_Instant_OrNull(entry["expiresUtc"]);

        // The expiry is REQUIRED, deliberately. A button whose deadline could not be read is the
        // exact shape this stage exists to stop: a live tap with nothing bounding it.
        if (data == null || optionText == null || questionText == null || expiresUtc == null)
            return null;

        return new PendingButtonRecord
        {
            Data = data,
            ThreadId = Read_Long_OrNull(entry["threadId"]),
            OptionText = optionText,
            GroupId = Read_Long_OrNull(entry["groupId"]) ?? 0,
            QuestionText = questionText,
            ExpiresUtc = expiresUtc.Value,
            IsHighRisk = Read_Bool_OrNull(entry["isHighRisk"]) ?? false,

            // ABSENT READS AS FALSE, which is the honest default for a state file written before
            // this field existed: every button in it was an answer, and an answer consumes.
            //
            // AND THE OLD NAME IS STILL READ. `keepsGroupOpen` was the same button under the
            // behaviour this replaced, so a state file written by the previous build maps onto the
            // new record exactly rather than turning a "Let's talk" into an option whose whole
            // instruction text gets stamped over the question as if it had been chosen.
            AnswersNothing = Read_Bool_OrNull(entry["answersNothing"])
                ?? Read_Bool_OrNull(entry["keepsGroupOpen"])
                ?? false,
        };
    }

    static OpenQuestionRecord? Read_Question_OrNull(JsonObject entry)
    {
        var messageId = Read_Long_OrNull(entry["messageId"]);
        var orchId = Read_String_OrNull(entry["orchId"]);
        var text = Read_String_OrNull(entry["text"]);
        var askedUtc = Read_Instant_OrNull(entry["askedUtc"]);

        if (messageId == null || orchId == null || text == null || askedUtc == null)
            return null;

        var isHighRisk = Read_Bool_OrNull(entry["isHighRisk"]) ?? false;

        return new OpenQuestionRecord
        {
            MessageId = messageId.Value,
            OrchId = orchId,
            Text = text,

            // Absent in a file written before it existed: that question simply cannot be recognised
            // as repeated, which is the old behaviour and nothing worse.
            Prompt = Read_String_OrNull(entry["prompt"]),
            AskedUtc = askedUtc.Value,
            ButtonGroupId = Read_Long_OrNull(entry["buttonGroupId"]) ?? 0,
            DeadlineUtc = Read_Instant_OrNull(entry["deadlineUtc"]),

            // A HIGH-RISK QUESTION CANNOT CARRY A DEFAULT, and that is enforced on the way IN as
            // well as on the way out. A file hand-edited (or written by a build that let one
            // through) must not be able to arm an unattended approval of a push.
            DefaultOptionIndex = isHighRisk ? null : Read_Int_OrNull(entry["defaultOptionIndex"]),
            IsHighRisk = isHighRisk,
            ReminderSent = Read_Bool_OrNull(entry["reminderSent"]) ?? false,

            // `inDiscussion` is no longer read. A file written by the previous build may carry it,
            // and a question that was "under discussion" there is an ordinary open question here:
            // under the new contract the tap that started the discussion already closed it, so
            // there is nothing for the flag to mean and the extra key is simply ignored.
        };
    }

    static PendingConfirmationRecord? Read_Confirmation_OrNull(JsonObject entry)
    {
        var code = Read_String_OrNull(entry["code"]);
        var orchId = Read_String_OrNull(entry["orchId"]);
        var optionText = Read_String_OrNull(entry["optionText"]);
        var questionText = Read_String_OrNull(entry["questionText"]);
        var expiresUtc = Read_Instant_OrNull(entry["expiresUtc"]);

        if (code == null || orchId == null || optionText == null || questionText == null || expiresUtc == null)
            return null;

        return new PendingConfirmationRecord
        {
            Code = code,
            ThreadId = Read_Long_OrNull(entry["threadId"]),
            MessageId = Read_Long_OrNull(entry["messageId"]),
            OrchId = orchId,
            OptionText = optionText,
            QuestionText = questionText,
            ExpiresUtc = expiresUtc.Value,
        };
    }

    /// <summary>
    /// THE DROP RULE INVERTS HERE, and only here. Everywhere else above, a record missing a field is
    /// dropped rather than defaulted, because a defaulted field is a DECISION taken by a bug — an
    /// expiry read as <c>default(DateTime)</c> refuses every tap, and read the other way it never
    /// expires. A close confirmation authorises nothing: it is never restored into the live registry
    /// and no tap is ever matched against it (see <see cref="CloseConfirmationRecord"/>). So a
    /// half-legible row can only mislead a human by DISAPPEARING, and the safe direction is to keep
    /// what is readable. Only the identity is required — a row that cannot say which request it is
    /// about is not a record of anything.
    /// </summary>
    static CloseConfirmationRecord? Read_CloseConfirmation_OrNull(JsonObject entry)
    {
        var parkedPath = Read_String_OrNull(entry["parkedPath"]);
        var orchId = Read_String_OrNull(entry["orchId"]);

        if (parkedPath == null || orchId == null)
            return null;

        return new CloseConfirmationRecord
        {
            ParkedPath = parkedPath,
            OrchId = orchId,
            Kind = Read_String_OrNull(entry["kind"]) ?? "unrecorded",
            MemberId = Read_String_OrNull(entry["memberId"]),
            Requester = Read_String_OrNull(entry["requester"]),
            AskedUtc = Read_Instant_OrNull(entry["askedUtc"]) ?? default,
            ExpiresUtc = Read_Instant_OrNull(entry["expiresUtc"]),
            PromptMessageId = Read_Long_OrNull(entry["promptMessageId"]),
        };
    }

    static IReadOnlyList<string> Read_StringList(JsonNode? node)
    {
        List<string> values = [];

        if (node is not JsonArray array)
            return values;

        foreach (var element in array)
        {
            var value = Read_String_OrNull(element);

            if (value != null)
                values.Add(value);
        }

        return values;
    }

    static IReadOnlyDictionary<string, string> Read_StringMap(JsonNode? node)
    {
        Dictionary<string, string> map = [];

        if (node is not JsonObject entry)
            return map;

        foreach (var pair in entry)
        {
            var value = Read_String_OrNull(pair.Value);

            if (value != null)
                map[pair.Key] = value;
        }

        return map;
    }

    static IReadOnlyDictionary<string, int> Read_IntMap(JsonNode? node)
    {
        Dictionary<string, int> map = [];

        if (node is not JsonObject entry)
            return map;

        foreach (var pair in entry)
        {
            var value = Read_Int_OrNull(pair.Value);

            if (value != null)
                map[pair.Key] = value.Value;
        }

        return map;
    }

    /// <summary>
    /// Unix seconds, the same units <see cref="Limits.LimitAlertState_Store"/> writes — one
    /// representation of an instant in this app's state files, never two.
    /// </summary>
    static long Write_Instant(DateTime instantUtc)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(instantUtc, DateTimeKind.Utc), TimeSpan.Zero).ToUnixTimeSeconds();
    }

    static DateTime? Read_Instant_OrNull(JsonNode? node)
    {
        var unixSeconds = Read_Long_OrNull(node);

        if (unixSeconds == null)
            return null;

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value).UtcDateTime;
        }
        catch
        {
            // Out of the representable range: unreadable, which the caller turns into a drop.
            return null;
        }
    }

    static string? Read_String_OrNull(JsonNode? node)
    {
        if (node == null)
            return null;

        try
        {
            return node.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    static long? Read_Long_OrNull(JsonNode? node)
    {
        if (node == null)
            return null;

        try
        {
            return node.GetValue<long>();
        }
        catch
        {
            return null;
        }
    }

    static int? Read_Int_OrNull(JsonNode? node)
    {
        var value = Read_Long_OrNull(node);

        if (value == null || value.Value > int.MaxValue || value.Value < int.MinValue)
            return null;

        return (int)value.Value;
    }

    static bool? Read_Bool_OrNull(JsonNode? node)
    {
        if (node == null)
            return null;

        try
        {
            return node.GetValue<bool>();
        }
        catch
        {
            return null;
        }
    }
}
