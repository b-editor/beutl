using System.Text.Json;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class DesignToolsTests
{
    [Test]
    public void Derive_palette_is_deterministic_for_identical_inputs()
    {
        var tools = new DesignTools(new AgentSessionManager());

        DerivePaletteResponse first = tools.DerivePalette(
            208,
            tonalSeed: "dark",
            harmonyScheme: "split-complementary",
            saturation: 0.61,
            derivationReason: "Ocean logistics brief maps to cool depth with warm signal motion.",
            structuralSignature: "diagonal editorial grid").Value!;
        DerivePaletteResponse second = tools.DerivePalette(
            208,
            tonalSeed: "dark",
            harmonyScheme: "split-complementary",
            saturation: 0.61,
            derivationReason: "Ocean logistics brief maps to cool depth with warm signal motion.",
            structuralSignature: "diagonal editorial grid").Value!;

        Assert.That(JsonSerializer.Serialize(second), Is.EqualTo(JsonSerializer.Serialize(first)));
    }

    [Test]
    public void Derive_palette_warns_when_creative_memory_matches_hue_band_or_structure()
    {
        string workspace = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        string global = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        var memory = new CreativeMemoryStore(workspace, globalRoot: global);
        memory.Record(new CreativeDirectionFingerprint(
            "Azure diagonal reveal",
            ["base hue: 210", "text-primary cool white"],
            ["drift", "reveal"],
            "diagonal editorial grid",
            DateTimeOffset.UtcNow));
        var tools = new DesignTools(new AgentSessionManager(memory));

        DerivePaletteResponse response = tools.DerivePalette(
            214,
            tonalSeed: "dark",
            harmonyScheme: "triadic",
            saturation: 0.58,
            derivationReason: "Technical ocean brief points to azure depth and diagonal parallax.",
            structuralSignature: "Diagonal editorial grid").Value!;

        Assert.Multiple(() =>
        {
            Assert.That(response.Warnings.Select(warning => warning.Kind), Does.Contain("hueBand"));
            Assert.That(response.Warnings.Select(warning => warning.Kind), Does.Contain("structuralSignature"));
            Assert.That(response.Warnings.SelectMany(warning => warning.MatchedConceptLabels), Does.Contain("Azure diagonal reveal"));
        });
    }

    [Test]
    public void Derive_palette_omits_repeat_warning_when_recent_memory_does_not_collide()
    {
        string workspace = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        string global = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        var memory = new CreativeMemoryStore(workspace, globalRoot: global);
        memory.Record(new CreativeDirectionFingerprint(
            "Amber poster stack",
            ["base hue: 45", "paper base"],
            ["fold"],
            "sequential poster stack",
            DateTimeOffset.UtcNow));
        var tools = new DesignTools(new AgentSessionManager(memory));

        DerivePaletteResponse response = tools.DerivePalette(
            214,
            tonalSeed: "dark",
            harmonyScheme: "triadic",
            saturation: 0.58,
            derivationReason: "Ocean brief points to azure depth and quiet drift.",
            structuralSignature: "split depth planes").Value!;

        Assert.That(
            response.Warnings.Select(warning => warning.Kind),
            Is.All.Not.EqualTo("hueBand").And.Not.EqualTo("structuralSignature"));
    }

    [Test]
    public void Derive_palette_reports_missing_derivation_reason()
    {
        var tools = new DesignTools(new AgentSessionManager());

        DerivePaletteResponse response = tools.DerivePalette(20).Value!;

        Assert.Multiple(() =>
        {
            Assert.That(response.DirectionReasonStatus, Is.EqualTo("missing"));
            Assert.That(response.Warnings.Select(warning => warning.Kind), Does.Contain("derivationReason"));
        });
    }

    [Test]
    public void Background_grammar_exposes_required_depth_bands_and_parametric_slots()
    {
        var tools = new DesignTools(new AgentSessionManager());

        BackgroundGrammarResponse response = tools.GetBackgroundGrammar("calm product reveal").Value!;

        Assert.Multiple(() =>
        {
            Assert.That(response.MinimumDepthBands.Select(band => band.Name), Is.EquivalentTo(new[] { "background", "midground", "foreground" }));
            Assert.That(response.BaseLayer.Options.Select(option => option.Name), Does.Contain("multi-stop gradient"));
            Assert.That(response.BaseLayer.Options.Select(option => option.Name), Does.Contain("shader"));
            Assert.That(response.DepthLayers, Has.Count.EqualTo(2));
            Assert.That(response.DepthLayers.SelectMany(layer => layer.Options).Select(option => option.Name), Does.Contain("particles"));
            Assert.That(response.DepthLayers.SelectMany(layer => layer.Options).Select(option => option.Name), Does.Contain("geometric accents"));
            Assert.That(response.DepthLayers.SelectMany(layer => layer.Options).Select(option => option.Name), Does.Contain("vignette"));
            Assert.That(response.Motion.Options.Select(option => option.Name), Is.EquivalentTo(new[] { "drift", "parallax" }));
            Assert.That(response.DerivationRules, Has.Some.Contains("Call derive_palette"));
            Assert.That(response.DeviationRules, Has.Some.Contains("recorded reason"));
            Assert.That(response.UsageHint, Does.Contain("not treat this response as JSON"));
        });
    }

    [Test]
    public void Get_started_references_palette_and_background_derivation_tools()
    {
        var tools = new QueryTools(new AgentSessionManager());

        GettingStartedResponse response = tools.GetStarted().Value!;

        Assert.Multiple(() =>
        {
            Assert.That(response.RecommendedCalls, Has.Some.Contains("derive_palette"));
            Assert.That(response.RecommendedCalls, Has.Some.Contains("get_background_grammar"));
            Assert.That(response.RecommendedCalls, Has.Some.Contains("hue/tone/motion vocabulary"));
        });
    }
}
