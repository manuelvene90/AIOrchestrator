using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Running.StreamTurn;

/// <summary>
/// One line of stdin: the message shape measured in §M9 and re-measured on 2.1.263 —
/// <c>{"type":"user","message":{"role":"user","content":[{"type":"text","text":"…"}]}}</c>.
/// Spelled once, here, because it is the whole input contract of the transport and it is not
/// documented anywhere: <c>--replay-user-messages</c> implies stdin can carry several messages but
/// never says what one looks like.
///
/// Serialised through <c>JsonObject</c> rather than string concatenation on purpose — the text is a
/// channel entry, so it carries newlines, quotes and em-dashes, and hand-built JSON is how those
/// become a transport error the owner reads as a session that stopped answering.
/// </summary>
public static class StreamUserMessage_Json
{
    public static string Build_Line(string text)
    {
        var message = new JsonObject
        {
            ["type"] = StreamEvent_Reader.TYPE_USER,
            ["message"] = new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            },
        };

        return message.ToJsonString();
    }
}
