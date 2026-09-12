using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Tests.Kit;
using Xunit;
using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsCatalog;

/// <summary>
/// THE PRESETS ARE DATA AND THE RENDERERS NEVER SPECIAL-CASE A NAME (spec §6.3), so everything that
/// could make a preset wrong has to be caught here: a path that is not in the catalogue, a value the
/// catalogue would refuse, and an embedded copy that has drifted from the kit copy — the same
/// two-copies problem `ChannelGrammarTests` closes for the channel grammar.
///
/// <para>
/// THE CATALOGUE IS REACHED THROUGH AN ALIAS, for the same reason <c>SettingsCatalogTests</c> needs
/// one: this file's own namespace ends in <c>SettingsCatalog</c>, so the bare word resolves to the
/// enclosing NAMESPACE before a using directive is ever consulted (CS0234), and every reference to
/// the catalogue type would fail. Not in the brief as written — the brief's own
/// <c>SettingsCatalog.Find_OrNull</c> does not compile from this namespace; fixed the same way the
/// sibling file already fixed it.
/// </para>
/// </summary>
public class PresetsLoaderTests
{
    [Theory]
    [InlineData(Presets_Loader.CLASSIC)]
    [InlineData(Presets_Loader.QUIET)]
    public void EveryPresetKey_IsACataloguePath_WithAValueTheCatalogueAccepts(string name)
    {
        var preset = Presets_Loader.Load_Embedded(name);

        foreach (var (key, value) in preset)
        {
            if (key.StartsWith('_'))
                continue;

            var definition = Catalog.Find_OrNull(key);

            Assert.True(definition != null, $"preset '{name}' names '{key}', which is not a catalogue path");
            Assert.Null(definition!.Validate_OrNull(value));
        }
    }

    /// <summary>
    /// A PRESET THAT RESTATES A SHIPPED DEFAULT IS A DEFAULT THAT CAN NEVER MOVE — the same reason
    /// the loader refuses to materialise reviewerModel into config.json. It also makes the origin the
    /// renderers show a lie: "from preset classic" for a value nobody chose.
    /// </summary>
    [Theory]
    [InlineData(Presets_Loader.CLASSIC)]
    [InlineData(Presets_Loader.QUIET)]
    public void NoPresetKey_RestatesItsShippedDefault(string name)
    {
        foreach (var (key, value) in Presets_Loader.Load_Embedded(name))
        {
            if (key.StartsWith('_'))
                continue;

            var shipped = Catalog.Find_OrNull(key)!.Default_OrNull;

            Assert.False(
                JsonNode.DeepEquals(value, shipped),
                $"preset '{name}' sets '{key}' to the shipped default — delete the line instead");
        }
    }

    /// <summary>
    /// ABSENT MEANS CLASSIC, because that is what master did before the merge (owner, spec §11.3).
    /// A blank value means the same: a cleared field is the owner saying nothing, which is the rule
    /// the model ladder already follows.
    /// </summary>
    [Fact]
    public void AConfigWithNoPresetKey_OrABlankOne_ResolvesToClassic()
    {
        Assert.Equal(Presets_Loader.CLASSIC, Presets_Loader.Resolve_ForConfig(null).Name);
        Assert.Equal(Presets_Loader.CLASSIC, Presets_Loader.Resolve_ForConfig((JsonObject)JsonNode.Parse("""{"repos":[]}""")!).Name);
        Assert.Equal(Presets_Loader.CLASSIC, Presets_Loader.Resolve_ForConfig((JsonObject)JsonNode.Parse("""{"preset":"  "}""")!).Name);
    }

    [Fact]
    public void AConfigNamingQuiet_ResolvesToTheQuietTree()
    {
        var resolved = Presets_Loader.Resolve_ForConfig((JsonObject)JsonNode.Parse("""{"preset":"quiet"}""")!);

        Assert.Equal(Presets_Loader.QUIET, resolved.Name);
        Assert.Equal("everything", resolved.Tree["phone.push"]!.GetValue<string>());
    }

    /// <summary>
    /// AN UNKNOWN NAME THROWS RATHER THAN DEFAULTING (spec §6.2) — the fork's own rule for
    /// planBackend.kind, and for its reason: "externa1" once read as the default and produced no
    /// error at all. A preset silently falling back to classic would give Nathan Manu's phone with
    /// nothing anywhere saying why.
    /// </summary>
    [Fact]
    public void AnUnknownPresetName_Throws_NamingTheWordAndTheKnownOnes()
    {
        var thrown = Assert.Throws<ArgumentException>(
            () => Presets_Loader.Resolve_ForConfig((JsonObject)JsonNode.Parse("""{"preset":"quite"}""")!));

        Assert.Contains("quite", thrown.Message);
        Assert.Contains("classic", thrown.Message);
        Assert.Contains("quiet", thrown.Message);
    }

    /// <summary>
    /// THE EMBEDDED COPY IS THE KIT COPY, byte for byte with line endings normalised — exactly what
    /// ChannelGrammarTests asserts for the grammar, and for the same reason: two files that are meant
    /// to be one drift silently, and only a test can tell them apart.
    /// </summary>
    [Theory]
    [InlineData(Presets_Loader.CLASSIC)]
    [InlineData(Presets_Loader.QUIET)]
    public void TheEmbeddedPresetIsTheKitsPreset(string name)
    {
        var onDisk = KitRepoFiles.Find(Path.Combine("kit", "presets", $"{name}.json"));

        Assert.True(onDisk != null, $"could not locate kit/presets/{name}.json walking up from '{AppContext.BaseDirectory}' — this test compares two copies, so a missing one means it compared nothing.");
        Assert.Equal(Normalise(File.ReadAllText(onDisk!)), Normalise(Presets_Loader.Embedded_Json(name)));
    }

    static string Normalise(string text) => text.Replace("\r\n", "\n").TrimEnd();
}
