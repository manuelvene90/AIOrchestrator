namespace AIOrchestratorCoreLib.Web;

/// <summary>
/// THE SETTINGS PAGE, READ FROM THE ONE COPY THERE IS (plan 04 Task 8, D14): <c>Web/Assets/settings.html</c>,
/// embedded in this assembly and nowhere else — the csproj comment beside the entry says why a second copy on
/// disk would be drift with nothing to catch it. The listener serves this at <c>GET /</c>; the page itself
/// knows no setting and draws everything from <c>GET /settings</c> (<see cref="SettingsRequest_Handler"/>).
///
/// <para>
/// A MISSING RESOURCE THROWS, because it is a broken BUILD, not a runtime condition — the grammar and the
/// presets take the same line (<c>ChannelGrammar</c>, <c>Presets_Loader</c>). The listener catches it per
/// request and answers 500, so a build without the page still serves the API.
/// </para>
/// </summary>
public static class SettingsPage_Reader
{
    public const string RESOURCE_NAME = "AIOrchestratorCoreLib.Web.Assets.settings.html";

    public const string CONTENT_TYPE = "text/html; charset=utf-8";

    static readonly Lazy<string> HTML = new(Read_Resource);

    /// <summary>The page, read once per process — it cannot change while the assembly is loaded.</summary>
    public static string Read_Html()
    {
        return HTML.Value;
    }

    static string Read_Resource()
    {
        using var stream = typeof(SettingsPage_Reader).Assembly.GetManifestResourceStream(RESOURCE_NAME)
            ?? throw new Exception(
                $"The settings page resource '{RESOURCE_NAME}' is not embedded in "
                + $"{typeof(SettingsPage_Reader).Assembly.GetName().Name}: check the EmbeddedResource entry in "
                + "AIOrchestratorCoreLib.csproj.");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
