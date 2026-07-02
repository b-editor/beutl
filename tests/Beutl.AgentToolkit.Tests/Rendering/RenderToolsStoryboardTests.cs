using System.Text.Json;
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
using ModelContextProtocol.Protocol;
using SkiaSharp;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class RenderToolsStoryboardTests
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task Render_storyboard_explicit_shots_writes_contact_sheet_and_visibility_analysis_without_motion_output()
    {
        string workspace = CreateWorkspace();
        Scene scene = CreateStaticStoryboardScene(workspace);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(workspace, session);

        CallToolResult call = await tools.RenderStoryboard(
            [
                new StoryboardShotInput("opening", 0.5),
                new StoryboardShotInput("detail", 1.5)
            ],
            outputDirectory: "storyboards",
            basename: "explicit",
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ReadToolResult<RenderStoryboardResult>(call);

        Assert.Multiple(() =>
        {
            Assert.That(call.Content, Has.Count.EqualTo(1));
            Assert.That(call.Content[0], Is.TypeOf<TextContentBlock>());
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.Status, Is.EqualTo("completed"));
            Assert.That(result.Value.JobId, Is.Null);
            Assert.That(result.Value.Result!.Shots, Has.Count.EqualTo(2));
            Assert.That(File.Exists(result.Value.Result.ContactSheetPath), Is.True);
            Assert.That(result.Value.Result.Shots.Select(shot => shot.StillPath), Has.All.Matches<string>(File.Exists));
            Assert.That(result.Value.Result.Shots.Select(shot => shot.VisibilityAnalysis), Has.All.Not.Null);
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

        CallToolResult call = await tools.RenderStoryboard(
            outputDirectory: "storyboards",
            basename: "auto",
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ReadToolResult<RenderStoryboardResult>(call);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.Status, Is.EqualTo("completed"));
            Assert.That(result.Value.Result!.Shots, Has.Count.EqualTo(scene.Children.Count));
            Assert.That(result.Value.Result.Shots.Select(shot => shot.Name), Is.EquivalentTo(scene.Children.Select(element => element.Name)));
            Assert.That(File.Exists(result.Value.Result.ContactSheetPath), Is.True);
        });
    }

    [Test]
    public async Task Render_storyboard_background_returns_running_job_then_completes()
    {
        AgentToolkitGpuTestEnvironment.EnsureAvailable();

        string workspace = CreateWorkspace();
        Scene scene = CreateStaticStoryboardScene(workspace);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(workspace, session);

        CallToolResult call = await tools.RenderStoryboard(
            outputDirectory: "storyboards",
            basename: "bg",
            background: true,
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> started = ReadToolResult<RenderStoryboardResult>(call);

        Assert.That(started.IsSuccess, Is.True, started.Error?.Message);
        Assert.That(started.Value!.Status, Is.EqualTo("running"));
        Assert.That(started.Value.JobId, Is.Not.Null.And.Not.Empty);
        Assert.That(started.Value.Result, Is.Null);

        string jobId = started.Value.JobId!;
        RenderJobSnapshot snapshot = await PollUntilTerminalAsync(tools, jobId);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.State, Is.EqualTo("completed"), snapshot.Error?.Message);
            Assert.That(snapshot.Result, Is.Not.Null);
            Assert.That(snapshot.CompletedAt, Is.Not.Null);
        });
    }

    [Test]
    public async Task Render_storyboard_return_image_content_appends_downscaled_contact_sheet()
    {
        string workspace = CreateWorkspace();
        Scene scene = CreateStaticStoryboardScene(workspace);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(workspace, session);

        CallToolResult call = await tools.RenderStoryboard(
            [
                new StoryboardShotInput("opening", 0.5),
                new StoryboardShotInput("detail", 1.5),
                new StoryboardShotInput("closing", 2.5)
            ],
            outputDirectory: "storyboards",
            basename: "with-image",
            returnImageContent: true,
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ReadToolResult<RenderStoryboardResult>(call);
        ImageContentBlock image = call.Content.OfType<ImageContentBlock>().Single();
        using SKBitmap bitmap = Decode(image);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(call.Content.OfType<TextContentBlock>(), Has.Exactly(1).Items);
            Assert.That(image.MimeType, Is.EqualTo("image/png"));
            Assert.That(Math.Max(bitmap.Width, bitmap.Height), Is.LessThanOrEqualTo(ImagePreviewEncoder.DefaultMaxLongEdge));
            Assert.That(File.Exists(result.Value!.Result!.ContactSheetPath), Is.True);
        });
    }

    [Test]
    public async Task Render_still_return_image_content_appends_downscaled_png()
    {
        string workspace = CreateWorkspace();
        var scene = new Scene(1600, 900, "large-still")
        {
            Duration = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(workspace, "Scene.scene"))
        };
        AddColorRectElement(scene, workspace, "large background", TimeSpan.Zero, TimeSpan.FromSeconds(1), 1600, 900, Colors.Red);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(workspace, session);

        CallToolResult call = await tools.RenderStill(
            "large-still.png",
            returnImageContent: true,
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStillResponse> result = ReadToolResult<RenderStillResponse>(call);
        ImageContentBlock image = call.Content.OfType<ImageContentBlock>().Single();
        using SKBitmap bitmap = Decode(image);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.Width, Is.EqualTo(1600));
            Assert.That(result.Value.Height, Is.EqualTo(900));
            Assert.That(call.Content.OfType<TextContentBlock>(), Has.Exactly(1).Items);
            Assert.That(image.MimeType, Is.EqualTo("image/png"));
            Assert.That(Math.Max(bitmap.Width, bitmap.Height), Is.EqualTo(ImagePreviewEncoder.DefaultMaxLongEdge));
        });
    }

    [Test]
    public async Task Render_storyboard_rejects_image_content_for_background_job()
    {
        string workspace = CreateWorkspace();
        Scene scene = CreateStaticStoryboardScene(workspace);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(workspace, session);

        CallToolResult call = await tools.RenderStoryboard(
            outputDirectory: "storyboards",
            basename: "bg-with-image",
            background: true,
            returnImageContent: true,
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ReadToolResult<RenderStoryboardResult>(call);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(call.Content.OfType<ImageContentBlock>(), Is.Empty);
        });
    }

    private static async Task<RenderJobSnapshot> PollUntilTerminalAsync(RenderTools tools, string jobId)
    {
        for (int attempt = 0; attempt < 300; attempt++)
        {
            ToolResult<RenderJobSnapshot> poll = tools.ReadRenderJob(jobId);
            Assert.That(poll.IsSuccess, Is.True, poll.Error?.Message);
            if (poll.Value!.State != "running")
            {
                return poll.Value;
            }

            await Task.Delay(100);
        }

        Assert.Fail($"Render job '{jobId}' did not reach a terminal state in time.");
        throw new InvalidOperationException("unreachable");
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
            new VideoExporter(new EncoderRegistration()),
            new RenderJobManager());
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

    private static ToolResult<T> ReadToolResult<T>(CallToolResult result)
    {
        TextContentBlock text = result.Content.OfType<TextContentBlock>().Single();
        return JsonSerializer.Deserialize<ToolResult<T>>(text.Text, s_jsonOptions)
               ?? throw new InvalidOperationException("Tool result JSON could not be deserialized.");
    }

    private static SKBitmap Decode(ImageContentBlock image)
    {
        return SKBitmap.Decode(image.DecodedData.ToArray())
               ?? throw new InvalidOperationException("Image content block was not a decodable PNG.");
    }
}
