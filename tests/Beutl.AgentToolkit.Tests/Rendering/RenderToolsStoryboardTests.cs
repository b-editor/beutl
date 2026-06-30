using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class RenderToolsStoryboardTests
{
    [Test]
    public async Task Render_storyboard_explicit_shots_writes_contact_sheet_and_visibility_analysis_without_motion_output()
    {
        string workspace = CreateWorkspace();
        Scene scene = CreateStaticStoryboardScene(workspace);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(workspace, session);

        ToolResult<RenderStoryboardResponse> result = await tools.RenderStoryboard(
            [
                new StoryboardShotInput("opening", 0.5),
                new StoryboardShotInput("detail", 1.5)
            ],
            outputDirectory: "storyboards",
            basename: "explicit",
            cancellationToken: CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.Shots, Has.Count.EqualTo(2));
            Assert.That(File.Exists(result.Value.ContactSheetPath), Is.True);
            Assert.That(result.Value.Shots.Select(shot => shot.StillPath), Has.All.Matches<string>(File.Exists));
            Assert.That(result.Value.Shots.Select(shot => shot.VisibilityAnalysis), Has.All.Not.Null);
            Assert.That(typeof(RenderStoryboardResponse).GetProperties().Select(property => property.Name), Has.None.Contains("Motion"));
            Assert.That(result.Error, Is.Null);
        });
    }

    [Test]
    public async Task Render_storyboard_auto_derived_shots_returns_one_entry_per_element()
    {
        string workspace = CreateWorkspace();
        Scene scene = CreateStaticStoryboardScene(workspace);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(workspace, session);

        ToolResult<RenderStoryboardResponse> result = await tools.RenderStoryboard(
            outputDirectory: "storyboards",
            basename: "auto",
            cancellationToken: CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.Shots, Has.Count.EqualTo(scene.Children.Count));
            Assert.That(result.Value.Shots.Select(shot => shot.Name), Is.EquivalentTo(scene.Children.Select(element => element.Name)));
            Assert.That(File.Exists(result.Value.ContactSheetPath), Is.True);
        });
    }

    [Test]
    public async Task Evaluate_edit_quality_static_layout_true_passes_without_major_motion_continuity_issue()
    {
        string workspace = CreateWorkspace();
        using var session = new AgentToolkitTestSession(CreateStaticQualityScene(workspace));
        RenderTools tools = CreateTools(workspace, session);

        ToolResult<QualityReviewResponse> result = await tools.EvaluateEditQuality(
            sampleCount: 3,
            staticLayout: true,
            cancellationToken: CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.PassesQualityGate, Is.True);
            Assert.That(result.Value.Issues, Has.None.Matches<QualityIssue>(issue =>
                issue.Category == "motionContinuity" && issue.Severity == "major"));
        });
    }

    [Test]
    public async Task Evaluate_edit_quality_static_layout_false_reports_major_motion_continuity_blocker()
    {
        string workspace = CreateWorkspace();
        using var session = new AgentToolkitTestSession(CreateStaticQualityScene(workspace));
        RenderTools tools = CreateTools(workspace, session);

        ToolResult<QualityReviewResponse> result = await tools.EvaluateEditQuality(
            sampleCount: 3,
            staticLayout: false,
            cancellationToken: CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.PassesQualityGate, Is.False);
            Assert.That(result.Value.Issues, Has.Some.Matches<QualityIssue>(issue =>
                issue.Category == "motionContinuity" && issue.Severity == "major"));
        });
    }

    [Test]
    public async Task Final_preflight_static_layout_true_is_ready_for_storyboard_without_motion_blockers()
    {
        string workspace = CreateWorkspace();
        using var session = new AgentToolkitTestSession(CreateStaticQualityScene(workspace));
        RenderTools tools = CreateTools(workspace, session);

        ToolResult<FinalPreflightResponse> result = await tools.FinalPreflight(
            outputPrefix: "preflight/static",
            sampleCount: 3,
            staticLayout: true,
            requireAnimatedProperties: true,
            cancellationToken: CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.ReadyForStoryboard, Is.True);
            Assert.That(result.Value.ReadyForExport, Is.False);
            Assert.That(result.Value.Motion, Is.Null);
            Assert.That(result.Value.Blockers, Has.None.Contains("Motion"));
            Assert.That(result.Value.Blockers, Has.None.Contains("animatedPropertyCount"));
        });
    }

    [Test]
    public async Task Final_preflight_static_layout_false_is_not_ready_and_reports_motion_blockers()
    {
        string workspace = CreateWorkspace();
        using var session = new AgentToolkitTestSession(CreateStaticQualityScene(workspace));
        RenderTools tools = CreateTools(workspace, session);

        ToolResult<FinalPreflightResponse> result = await tools.FinalPreflight(
            outputPrefix: "preflight/motion",
            sampleCount: 3,
            staticLayout: false,
            requireAnimatedProperties: true,
            cancellationToken: CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.ReadyForExport, Is.False);
            Assert.That(result.Value.ReadyForStoryboard, Is.False);
            Assert.That(result.Value.Motion, Is.Not.Null);
            Assert.That(result.Value.Blockers, Has.Some.Contains("Motion variation did not pass"));
            Assert.That(result.Value.Blockers, Has.Some.Contains("animatedPropertyCount is 0"));
        });
    }

    private static RenderTools CreateTools(string workspace, AgentToolkitTestSession session)
    {
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var stillRenderer = new StillRenderer();
        var motionVariationAnalyzer = new MotionVariationAnalyzer(stillRenderer);
        return new RenderTools(
            manager,
            new WorkspaceGuard(workspace),
            new DestructiveGuard(),
            stillRenderer,
            new StoryboardRenderer(),
            motionVariationAnalyzer,
            new QualityAnalyzer(motionVariationAnalyzer),
            new VideoExporter(new EncoderRegistration()));
    }

    private static Scene CreateStaticStoryboardScene(string workspace)
    {
        var scene = new Scene(320, 180, "storyboard")
        {
            Duration = TimeSpan.FromSeconds(3),
            Uri = new Uri(Path.Combine(workspace, "Scene.scene"))
        };
        AddColorRectElement(scene, workspace, "opening", TimeSpan.Zero, TimeSpan.FromSeconds(1), 320, 180, Colors.Red);
        AddColorRectElement(scene, workspace, "middle", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 320, 180, Colors.Blue);
        AddColorRectElement(scene, workspace, "closing", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), 320, 180, Colors.Green);
        return scene;
    }

    private static Scene CreateStaticQualityScene(string workspace)
    {
        var scene = new Scene(320, 180, "static-quality")
        {
            Duration = TimeSpan.FromSeconds(3),
            Uri = new Uri(Path.Combine(workspace, "Scene.scene"))
        };
        AddColorRectElement(scene, workspace, "Background plate", TimeSpan.Zero, TimeSpan.FromSeconds(3), 320, 180, Color.Parse("#ff20242b"));
        AddColorRoundedRectElement(scene, workspace, "Title backing plate", TimeSpan.Zero, TimeSpan.FromSeconds(3), 120, 44, Color.Parse("#eeedf0e8"));
        AddTextElement(scene, workspace, "Launch notes", TimeSpan.Zero, TimeSpan.FromSeconds(3));
        AddEllipseElement(scene, workspace, "Soft accent", TimeSpan.Zero, TimeSpan.FromSeconds(3));
        return scene;
    }

    private static void AddColorRectElement(
        Scene scene,
        string workspace,
        string name,
        TimeSpan start,
        TimeSpan length,
        float width,
        float height,
        Color color)
    {
        var rect = new RectShape
        {
            Name = name,
            Width = { CurrentValue = width },
            Height = { CurrentValue = height },
            Fill = { CurrentValue = new SolidColorBrush(color) }
        };
        AddElement(scene, workspace, name, start, length, scene.Children.Count, rect);
    }

    private static void AddColorRoundedRectElement(
        Scene scene,
        string workspace,
        string name,
        TimeSpan start,
        TimeSpan length,
        float width,
        float height,
        Color color)
    {
        var rect = new RoundedRectShape
        {
            Name = name,
            Width = { CurrentValue = width },
            Height = { CurrentValue = height },
            CornerRadius = { CurrentValue = new CornerRadius(12) },
            Fill = { CurrentValue = new SolidColorBrush(color) }
        };
        AddElement(scene, workspace, name, start, length, scene.Children.Count, rect);
    }

    private static void AddTextElement(Scene scene, string workspace, string text, TimeSpan start, TimeSpan length)
    {
        var block = new TextBlock
        {
            Name = text,
            Text = { CurrentValue = text },
            Size = { CurrentValue = 24 },
            Fill = { CurrentValue = new SolidColorBrush(Color.Parse("#ff1a2028")) }
        };
        AddElement(scene, workspace, text, start, length, 10, block);
    }

    private static void AddEllipseElement(Scene scene, string workspace, string name, TimeSpan start, TimeSpan length)
    {
        var ellipse = new EllipseShape
        {
            Name = name,
            Width = { CurrentValue = 42 },
            Height = { CurrentValue = 42 },
            Fill = { CurrentValue = new SolidColorBrush(Color.Parse("#ffb85c38")) },
            Transform =
            {
                CurrentValue = new TransformGroup
                {
                    Children = { new TranslateTransform(74, -46) }
                }
            }
        };
        AddElement(scene, workspace, name, start, length, 6, ellipse);
    }

    private static void AddElement(
        Scene scene,
        string workspace,
        string name,
        TimeSpan start,
        TimeSpan length,
        int zIndex,
        EngineObject obj)
    {
        var element = new Element
        {
            Name = name,
            Start = start,
            Length = length,
            ZIndex = zIndex,
            Uri = new Uri(Path.Combine(workspace, $"{name}.belm"))
        };
        element.AddObject(obj);
        scene.Children.Add(element);
    }

    private static string CreateWorkspace()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
