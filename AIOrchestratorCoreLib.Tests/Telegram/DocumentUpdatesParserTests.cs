using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// A document is a message, with or without words. Before this was fixed, a file sent with no
/// caption made the whole update parse to nothing: no owner message, no log line, and the offset
/// moved on regardless — the owner had sent a file and the bridge had never heard of it.
/// </summary>
public class DocumentUpdatesParserTests
{
    const long SUPERGROUP_ID = -1001234567890;
    const long OWNER_ID = 42;

    [Fact]
    public void ADocumentWithNoCaption_StillProducesOneOwnerMessageCarryingTheDocument()
    {
        var json = """
            {
              "ok": true,
              "result": [
                {
                  "update_id": 400,
                  "message": {
                    "message_id": 20,
                    "from": { "id": 42 },
                    "chat": { "id": -1001234567890 },
                    "message_thread_id": 7,
                    "document": {
                      "file_id": "doc-abc",
                      "file_name": "report.csv",
                      "mime_type": "text/csv",
                      "file_size": 1234
                    }
                  }
                }
              ]
            }
            """;

        var batch = TelegramUpdates_Parser.Parse_OwnerMessages(json, SUPERGROUP_ID, OWNER_ID);

        Assert.Single(batch.OwnerMessages);
        Assert.NotNull(batch.OwnerMessages[0].Document);
        Assert.Equal("doc-abc", batch.OwnerMessages[0].Document!.FileId);
        Assert.Equal(string.Empty, batch.OwnerMessages[0].Text);
    }

    [Fact]
    public void ADocumentWithACaption_HasTheCaptionAsTextAndTheDocumentPresent()
    {
        var json = """
            {
              "ok": true,
              "result": [
                {
                  "update_id": 401,
                  "message": {
                    "message_id": 21,
                    "from": { "id": 42 },
                    "chat": { "id": -1001234567890 },
                    "message_thread_id": 7,
                    "caption": "here is the export",
                    "document": {
                      "file_id": "doc-def",
                      "file_name": "export.json",
                      "mime_type": "application/json",
                      "file_size": 999
                    }
                  }
                }
              ]
            }
            """;

        var batch = TelegramUpdates_Parser.Parse_OwnerMessages(json, SUPERGROUP_ID, OWNER_ID);

        Assert.Single(batch.OwnerMessages);
        Assert.Equal("here is the export", batch.OwnerMessages[0].Text);
        Assert.NotNull(batch.OwnerMessages[0].Document);
        Assert.Equal("doc-def", batch.OwnerMessages[0].Document!.FileId);
    }

    [Fact]
    public void TheDocumentsFileNameMimeTypeAndFileSize_AreCarriedThrough()
    {
        var json = """
            {
              "ok": true,
              "result": [
                {
                  "update_id": 402,
                  "message": {
                    "message_id": 22,
                    "from": { "id": 42 },
                    "chat": { "id": -1001234567890 },
                    "message_thread_id": 7,
                    "document": {
                      "file_id": "doc-ghi",
                      "file_name": "notes.pdf",
                      "mime_type": "application/pdf",
                      "file_size": 55555
                    }
                  }
                }
              ]
            }
            """;

        var batch = TelegramUpdates_Parser.Parse_OwnerMessages(json, SUPERGROUP_ID, OWNER_ID);

        var document = batch.OwnerMessages[0].Document!;
        Assert.Equal("notes.pdf", document.FileName);
        Assert.Equal("application/pdf", document.MimeType);
        Assert.Equal(55555, document.SizeBytes);
    }

    [Fact]
    public void ADocumentObjectWithNoFileId_IsTreatedAsAbsent()
    {
        var json = """
            {
              "ok": true,
              "result": [
                {
                  "update_id": 403,
                  "message": {
                    "message_id": 23,
                    "from": { "id": 42 },
                    "chat": { "id": -1001234567890 },
                    "message_thread_id": 7,
                    "caption": "still has words",
                    "document": {
                      "file_name": "mystery.bin"
                    }
                  }
                }
              ]
            }
            """;

        var batch = TelegramUpdates_Parser.Parse_OwnerMessages(json, SUPERGROUP_ID, OWNER_ID);

        Assert.Single(batch.OwnerMessages);
        Assert.Null(batch.OwnerMessages[0].Document);
        Assert.Equal("still has words", batch.OwnerMessages[0].Text);
    }

    /// <summary>A document with no file_id AND no text/caption/photo/voice leaves nothing for the
    /// update to carry, so the whole update yields no owner message — same as any other empty
    /// update.</summary>
    [Fact]
    public void ADocumentWithNoFileIdAndNoText_YieldsNoOwnerMessageAtAll()
    {
        var json = """
            {
              "ok": true,
              "result": [
                {
                  "update_id": 404,
                  "message": {
                    "message_id": 24,
                    "from": { "id": 42 },
                    "chat": { "id": -1001234567890 },
                    "message_thread_id": 7,
                    "document": {
                      "file_name": "mystery.bin"
                    }
                  }
                }
              ]
            }
            """;

        var batch = TelegramUpdates_Parser.Parse_OwnerMessages(json, SUPERGROUP_ID, OWNER_ID);

        Assert.Empty(batch.OwnerMessages);
        Assert.Equal(404, batch.MaxUpdateId);
    }

    [Fact]
    public void ATextOnlyMessage_StillHasANullDocument()
    {
        var json = """
            {
              "ok": true,
              "result": [
                {
                  "update_id": 405,
                  "message": {
                    "message_id": 25,
                    "from": { "id": 42 },
                    "chat": { "id": -1001234567890 },
                    "message_thread_id": 7,
                    "text": "just words, no file"
                  }
                }
              ]
            }
            """;

        var batch = TelegramUpdates_Parser.Parse_OwnerMessages(json, SUPERGROUP_ID, OWNER_ID);

        Assert.Single(batch.OwnerMessages);
        Assert.Null(batch.OwnerMessages[0].Document);
        Assert.Equal("just words, no file", batch.OwnerMessages[0].Text);
    }

    /// <summary>The update id / max update id behaviour is unchanged by the document fix: a
    /// filtered-out update (from someone else) still advances the offset.</summary>
    [Fact]
    public void MaxUpdateId_StillAdvancesOverAFilteredOutDocumentUpdate()
    {
        var json = """
            {
              "ok": true,
              "result": [
                {
                  "update_id": 500,
                  "message": {
                    "message_id": 26,
                    "from": { "id": 999 },
                    "chat": { "id": -1001234567890 },
                    "message_thread_id": 7,
                    "document": {
                      "file_id": "doc-jkl",
                      "file_name": "not-owners.txt"
                    }
                  }
                },
                {
                  "update_id": 501,
                  "message": {
                    "message_id": 27,
                    "from": { "id": 42 },
                    "chat": { "id": -1001234567890 },
                    "message_thread_id": 7,
                    "document": {
                      "file_id": "doc-mno",
                      "file_name": "owners.txt"
                    }
                  }
                }
              ]
            }
            """;

        var batch = TelegramUpdates_Parser.Parse_OwnerMessages(json, SUPERGROUP_ID, OWNER_ID);

        Assert.Single(batch.OwnerMessages);
        Assert.Equal("owners.txt", batch.OwnerMessages[0].Document!.FileName);
        Assert.Equal(501, batch.MaxUpdateId);
    }
}
