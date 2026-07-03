using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class PlanOriginalScaffoldTests
{
    [Test]
    public async Task Plan_original_scaffold_applies_cleanly_is_not_a_template_and_passes_quality_gate()
    {
        string root = CreateWorkspace();
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        var sessionTools = new SessionTools(source, manager, new WorkspaceGuard(root), new DestructiveGuard());
        var queryTools = new QueryTools(manager);
        var editTools = new EditTools(manager);

        ToolResult<CreateProjectResponse> created = sessionTools.CreateProject(
            "scaffold.bep",
            width: 1920,
            height: 1080,
            frameRate: 30,
            duration: "00:00:08");
        Assert.That(created.IsSuccess, Is.True, created.Error?.Message);

        ToolResult<OriginalScaffoldResponse> planned = queryTools.PlanOriginalScaffold(brief: "calm product reveal", seed: "seed-apply");
        Assert.That(planned.IsSuccess, Is.True, planned.Error?.Message);
        OriginalScaffold scaffold = planned.Value!.Scaffold;

        Assert.That(CompositionTemplateCatalog.TryInferTemplateName(scaffold.Patch), Is.Null);

        ToolResult<ApplyEditResponse> applied = editTools.ApplyEdit(patch: scaffold.Patch, schemaVersion: "1");
        Assert.That(applied.IsSuccess, Is.True, applied.Error?.Message);

        var scene = (Scene)manager.RequireSession().Root;
        Assert.That(scene.Children, Has.Count.GreaterThanOrEqualTo(4));
        Assert.That(scene.Children.All(element => element.Objects.Count == 1), Is.True);

        var stillRenderer = new StillRenderer();
        QualityReviewResponse review = await new QualityAnalyzer(new MotionVariationAnalyzer(stillRenderer), stillRenderer)
            .AnalyzeAsync(
                scene,
                timeSeconds: null,
                sampleCount: 3,
                renderScale: 1,
                styleProfile: null,
                allowAllCaps: false,
                allowHardCuts: false,
                allowRectDominance: false,
                relaxAesthetics: false,
                allowStillness: false,
                allowDenseText: false,
                allowMultiObjectElements: false,
                allowMonochrome: false,
                allowMinimalDensity: false,
                plannedForegroundElementsPerShot: 0,
                evaluateMotion: false,
                cancellationToken: CancellationToken.None);

        Assert.That(
            review.Issues.Where(issue => issue.Severity is "major" or "critical"),
            Is.Empty,
            string.Join("; ", review.Issues.Select(issue => $"{issue.Severity}:{issue.Category}")));
        Assert.That(review.PassesQualityGate, Is.True);
    }

    [Test]
    public void Plan_original_scaffold_varies_by_seed()
    {
        var manager = new AgentSessionManager();
        var queryTools = new QueryTools(manager);

        OriginalScaffold first = queryTools.PlanOriginalScaffold(brief: null, seed: "alpha-seed").Value!.Scaffold;
        OriginalScaffold second = queryTools.PlanOriginalScaffold(brief: null, seed: "omega-seed").Value!.Scaffold;

        Assert.Multiple(() =>
        {
            Assert.That(second.StructuralSignature, Is.Not.EqualTo(first.StructuralSignature));
            Assert.That(second.Patch.ToJsonString(), Is.Not.EqualTo(first.Patch.ToJsonString()));

            string firstAccent = RoleColor(first, "accent");
            string secondAccent = RoleColor(second, "accent");
            Assert.That(secondAccent, Is.Not.EqualTo(firstAccent));
        });
    }

    private static string RoleColor(OriginalScaffold scaffold, string role)
    {
        return scaffold.PaletteRoles.Single(item => item.Role == role).Color;
    }

    private static string CreateWorkspace()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
