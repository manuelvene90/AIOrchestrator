using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

public class TelegramUpdatesParserTests
{
    const long SUPERGROUP_ID = -1001234567890;
    const long OWNER_ID = 42;

    const string MIXED_UPDATES_JSON = """
        {
          "ok": true,
          "result": [
            {
              "update_id": 100,
              "message": {
                "message_id": 1,
                "from": { "id": 42 },
                "chat": { "id": -1001234567890 },
                "message_thread_id": 7,
                "text": "please review the drift guard"
              }
            },
            {
              "update_id": 101,
              "message": {
                "message_id": 2,
                "from": { "id": 999 },
                "chat": { "id": -1001234567890 },
                "message_thread_id": 7,
                "text": "message from someone else"
              }
            },
            {
              "update_id": 102,
              "message": {
                "message_id": 3,
                "from": { "id": 42 },
                "chat": { "id": -1001234567890 },
                "text": "general topic message"
              }
            },
            {
              "update_id": 103,
              "message": {
                "message_id": 4,
                "from": { "id": 42 },
                "chat": { "id": -555 },
                "text": "message in another chat"
              }
            }
          ]
        }
        """;

    [Fact]
    public void Parse_OwnerMessages_FiltersToOwnerInSupergroupOnly()
    {
        var batch = TelegramUpdates_Parser.Parse_OwnerMessages(MIXED_UPDATES_JSON, SUPERGROUP_ID, OWNER_ID);

        Assert.Equal(2, batch.OwnerMessages.Count);
        Assert.Equal("please review the drift guard", batch.OwnerMessages[0].Text);
        Assert.Equal(7, batch.OwnerMessages[0].MessageThreadId);
        Assert.Equal("general topic message", batch.OwnerMessages[1].Text);
        Assert.Null(batch.OwnerMessages[1].MessageThreadId);
    }

    [Fact]
    public void Parse_OwnerMessages_MaxUpdateId_CoversFilteredOutUpdatesToo()
    {
        var batch = TelegramUpdates_Parser.Parse_OwnerMessages(MIXED_UPDATES_JSON, SUPERGROUP_ID, OWNER_ID);

        Assert.Equal(103, batch.MaxUpdateId);
    }

    [Fact]
    public void Parse_OwnerMessages_EmptyResult_NullMaxUpdateId()
    {
        var batch = TelegramUpdates_Parser.Parse_OwnerMessages("""{"ok":true,"result":[]}""", SUPERGROUP_ID, OWNER_ID);

        Assert.Null(batch.MaxUpdateId);
        Assert.Empty(batch.OwnerMessages);
    }

    [Fact]
    public void Parse_OwnerMessages_GarbageJson_ReturnsEmptyBatch()
    {
        var batch = TelegramUpdates_Parser.Parse_OwnerMessages("[1,2,3]", SUPERGROUP_ID, OWNER_ID);

        Assert.Null(batch.MaxUpdateId);
        Assert.Empty(batch.OwnerMessages);
    }

    [Fact]
    public void Parse_CallbackTaps_OwnerTapsOnly_WithThreadAndData()
    {
        var json = """
            {
              "ok": true,
              "result": [
                {
                  "update_id": 200,
                  "callback_query": {
                    "id": "cbq-1",
                    "from": { "id": 42 },
                    "data": "opt-7",
                    "message": { "message_id": 9, "message_thread_id": 7 }
                  }
                },
                {
                  "update_id": 201,
                  "callback_query": {
                    "id": "cbq-2",
                    "from": { "id": 999 },
                    "data": "opt-8",
                    "message": { "message_id": 10 }
                  }
                }
              ]
            }
            """;

        var batch = TelegramUpdates_Parser.Parse_OwnerMessages(json, SUPERGROUP_ID, OWNER_ID);

        Assert.Single(batch.CallbackTaps);
        Assert.Equal("cbq-1", batch.CallbackTaps[0].CallbackQueryId);
        Assert.Equal("opt-7", batch.CallbackTaps[0].Data);
        Assert.Equal(7, batch.CallbackTaps[0].MessageThreadId);
        Assert.Equal(201, batch.MaxUpdateId);
    }

    [Fact]
    public void Parse_VoiceMessage_CarriesTheVoiceFileId()
    {
        var json = """
            {
              "ok": true,
              "result": [
                {
                  "update_id": 300,
                  "message": {
                    "message_id": 11,
                    "from": { "id": 42 },
                    "chat": { "id": -1001234567890 },
                    "message_thread_id": 7,
                    "voice": { "file_id": "voice-abc", "duration": 4 }
                  }
                }
              ]
            }
            """;

        var batch = TelegramUpdates_Parser.Parse_OwnerMessages(json, SUPERGROUP_ID, OWNER_ID);

        Assert.Single(batch.OwnerMessages);
        Assert.Equal("voice-abc", batch.OwnerMessages[0].VoiceFileId);
        Assert.Equal(string.Empty, batch.OwnerMessages[0].Text);
    }
}
