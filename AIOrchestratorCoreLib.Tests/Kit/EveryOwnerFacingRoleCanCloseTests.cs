using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// A ROLE THAT TALKS TO THE OWNER MUST KNOW HOW TO END ITS OWN ORCHESTRATION.
///
/// The mechanism was always there — any session can drop a close-orchestration request and the app
/// holds it for the owner's confirming tap. `supervisor.md` taught it; `solo.md` never did. So on
/// 2026-08-19 the owner told a solo "close this session", and it answered "close the orchestration
/// from the app when you're ready" — advice that is useless to someone on a phone, and the
/// orchestration simply stayed open.
///
/// A capability that exists and is untaught is indistinguishable, from the owner's side, from one
/// that does not exist.
/// </summary>
public class EveryOwnerFacingRoleCanCloseTests
{
    /// <summary>The roles that take instructions straight from the owner, so both can be told to close.</summary>
    static readonly string[] OWNER_FACING_COMMANDS = ["solo.md", "supervisor.md"];

    [Fact]
    public void BothOwnerFacingRolesAreTaughtToCloseTheirOwnOrchestration()
    {
        var files = Find_RoleCommandFiles();

        // The harness proves itself before asserting a presence: a scan that found nothing would be
        // the strongest possible pass and would mean nothing at all.
        Assert.NotEmpty(files);

        foreach (var expected in OWNER_FACING_COMMANDS)
        {
            var path = files.FirstOrDefault(file => Path.GetFileName(file) == expected);

            Assert.True(path != null, $"{expected} is not in kit/commands — this test is not reading what it claims to");

            var text = File.ReadAllText(path!);

            Assert.True(
                text.Contains("close-orchestration"),
                $"{expected} never teaches the close-orchestration request, so that role cannot end its own orchestration when asked");

            Assert.True(
                text.Contains("requester"),
                $"{expected} teaches the close request without the required `requester` field, which the app rejects");
        }
    }

    /// <summary>
    /// THE ANSWER THAT CAUSED THIS, refused explicitly. Knowing the request exists is not enough if
    /// the role still believes "tell them to use the app" is an acceptable reply.
    /// </summary>
    [Fact]
    public void SoloIsToldNotToSendTheOwnerToTheApp()
    {
        var solo = Find_RoleCommandFiles().FirstOrDefault(file => Path.GetFileName(file) == "solo.md");

        Assert.True(solo != null, "solo.md is not in kit/commands");

        Assert.Contains("Never answer a close request by telling them to do it from the app", File.ReadAllText(solo!));
    }

    static IReadOnlyList<string> Find_RoleCommandFiles()
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var candidate = Path.Combine(folder, "kit", "commands");

            if (Directory.Exists(candidate))
                return [.. Directory.GetFiles(candidate, "*.md")];

            var parent = Directory.GetParent(folder);

            if (parent == null)
                break;

            folder = parent.FullName;
        }

        // A harness that cannot find its subject must fail loudly rather than certify an absence.
        Assert.Fail($"kit/commands was not found walking up from {AppContext.BaseDirectory}");

        return [];
    }
}
