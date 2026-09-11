using AIOrchestratorCoreLib.Kit;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// The reader against real files in a temp Claude home. Every fixture below is the SHAPE measured on
/// a real installation (installed_plugins.json keys an ARRAY per plugin id, because one plugin can
/// be installed at several scopes) — a fixture invented from the docs would let the reader pass
/// against a file no machine writes.
/// </summary>
public class InstalledPluginReaderTests : IDisposable
{
    readonly string _home = Path.Combine(Path.GetTempPath(), $"aiorch-home-{Guid.NewGuid():N}");

    public InstalledPluginReaderTests()
    {
        Directory.CreateDirectory(Path.Combine(_home, InstalledPlugin_Reader.PLUGINS_FOLDER));
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
            Directory.Delete(_home, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AFreshMachine_WithNoFilesAtAll_ReadsAsNotInstalled_NotAsUnreadable()
    {
        var reading = InstalledPlugin_Reader.Read(_home, KitPlugin.ID);

        Assert.False(reading.Is_Installed);
        Assert.Null(reading.UnreadableReason);
        Assert.False(reading.Enabled);
    }

    [Fact]
    public void AnInstalledEnabledPlugin_ReadsBackItsVersionAndWhereItLives()
    {
        Write_Installed("1.0.0", "/somewhere/cache/aiorch-local/aiorch/1.0.0");
        Write_Settings(enabled: true);

        var reading = InstalledPlugin_Reader.Read(_home, KitPlugin.ID);

        Assert.Equal("1.0.0", reading.Version);
        Assert.Equal("/somewhere/cache/aiorch-local/aiorch/1.0.0", reading.InstallPath);
        Assert.True(reading.Enabled);
        Assert.Null(reading.UnreadableReason);
    }

    [Fact]
    public void APluginInstalledButSwitchedOff_IsInstalledAndNotEnabled()
    {
        Write_Installed("1.0.0", "/somewhere");
        Write_Settings(enabled: false);

        var reading = InstalledPlugin_Reader.Read(_home, KitPlugin.ID);

        Assert.True(reading.Is_Installed);
        Assert.False(reading.Enabled);
    }

    [Fact]
    public void AnotherPluginsRecord_IsNotMistakenForOurs()
    {
        File.WriteAllText(Installed_File, """
            { "version": 2, "plugins": { "something-else@elsewhere": [ { "version": "9.9.9", "installPath": "/x" } ] } }
            """);

        var reading = InstalledPlugin_Reader.Read(_home, KitPlugin.ID);

        Assert.False(reading.Is_Installed);
        Assert.Null(reading.UnreadableReason);
    }

    [Fact]
    public void AnUnreadableInstallRecord_IsReportedAsUnreadable_NeverAsAbsence()
    {
        File.WriteAllText(Installed_File, "{ this is not json");

        var reading = InstalledPlugin_Reader.Read(_home, KitPlugin.ID);

        Assert.NotNull(reading.UnreadableReason);
        Assert.Contains(InstalledPlugin_Reader.INSTALLED_PLUGINS_FILE, reading.UnreadableReason);
    }

    /// <summary>
    /// A FIELD OF THE WRONG TYPE IS AN UNREADABLE RECORD, NOT AN ABSENT ONE — and above all it must
    /// not throw out of Read. GetValue&lt;string&gt;() on a number raises InvalidOperationException, and
    /// that call used to sit OUTSIDE the guarded block: the exception escaped the reader entirely,
    /// so the caller got neither answer and the whole startup check died with it. {"version": 2} is
    /// a shape a file on disk can genuinely hold — a hand-edit, a future CLI, a half-written file.
    /// </summary>
    [Fact]
    public void AnInstallRecordWithANumberWhereAStringBelongs_IsUnreadable_AndNeverThrows()
    {
        File.WriteAllText(Installed_File, $$"""
            { "version": 2, "plugins": { "{{KitPlugin.ID}}": [ { "version": 2, "installPath": "/x" } ] } }
            """);

        var reading = InstalledPlugin_Reader.Read(_home, KitPlugin.ID);

        Assert.NotNull(reading.UnreadableReason);
        Assert.False(reading.Is_Installed);
    }

    [Fact]
    public void AnEnabledPluginsFlagThatIsNotABool_IsUnreadable_AndNeverThrows()
    {
        Write_Installed("1.0.0", "/somewhere");
        File.WriteAllText(Path.Combine(_home, InstalledPlugin_Reader.SETTINGS_FILE), $$"""
            { "enabledPlugins": { "{{KitPlugin.ID}}": "yes" } }
            """);

        var reading = InstalledPlugin_Reader.Read(_home, KitPlugin.ID);

        Assert.NotNull(reading.UnreadableReason);
    }

    [Fact]
    public void AnUnreadableSettingsFile_IsReportedAsUnreadable_NeverAsDisabled()
    {
        Write_Installed("1.0.0", "/somewhere");
        File.WriteAllText(Path.Combine(_home, InstalledPlugin_Reader.SETTINGS_FILE), "{ nope");

        var reading = InstalledPlugin_Reader.Read(_home, KitPlugin.ID);

        Assert.NotNull(reading.UnreadableReason);
        Assert.Contains(InstalledPlugin_Reader.SETTINGS_FILE, reading.UnreadableReason);
    }

    string Installed_File => Path.Combine(_home, InstalledPlugin_Reader.PLUGINS_FOLDER, InstalledPlugin_Reader.INSTALLED_PLUGINS_FILE);

    void Write_Installed(string version, string installPath)
    {
        File.WriteAllText(Installed_File, $$"""
            {
              "version": 2,
              "plugins": {
                "{{KitPlugin.ID}}": [
                  { "scope": "user", "installPath": "{{installPath}}", "version": "{{version}}" }
                ]
              }
            }
            """);
    }

    void Write_Settings(bool enabled)
    {
        File.WriteAllText(Path.Combine(_home, InstalledPlugin_Reader.SETTINGS_FILE), $$"""
            { "enabledPlugins": { "{{KitPlugin.ID}}": {{(enabled ? "true" : "false")}} } }
            """);
    }
}
