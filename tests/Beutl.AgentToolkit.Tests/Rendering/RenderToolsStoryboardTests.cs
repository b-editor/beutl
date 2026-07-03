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
    public void Resolve_storyboard_frames_subdivides_explicit_shots_at_binary_points()
    {
        var scene = new Scene(16, 9, "subdivision") { Duration = TimeSpan.FromSeconds(1) };
        StoryboardShotInput[] shots =
        [
            new("left", 0),
            new("right", 1)
        ];

        RenderTools.ResolvedStoryboardFrame[] level1 = RenderTools.ResolveStoryboardFrames(scene, shots, 1).ToArray();
        RenderTools.ResolvedStoryboardFrame[] level2 = RenderTools.ResolveStoryboardFrames(scene, shots, 2).ToArray();
        RenderTools.ResolvedStoryboardFrame[] level3 = RenderTools.ResolveStoryboardFrames(scene, shots, 3).ToArray();

        Assert.Multiple(() =>
        {
            AssertTimes(level1, [0, 0.5, 1]);
            AssertTimes(level2, [0, 0.25, 0.5, 0.75, 1]);
            AssertTimes(level3, [0, 0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875, 1]);
            Assert.That(level2.Select(frame => frame.Kind), Is.EqualTo(new[]
            {
                "shot",
                "inbetween",
                "inbetween",
                "inbetween",
                "shot"
            }));
            Assert.That(level2.Select(frame => frame.SubdivisionLevel), Is.EqualTo(new[] { 0, 2, 1, 2, 0 }));
            Assert.That(level2[1].Name, Is.EqualTo("between:left~right@L2:1/4"));
            Assert.That(level2[2].Name, Is.EqualTo("between:left~right@L1:1/2"));
            Assert.That(level2[3].Name, Is.EqualTo("between:left~right@L2:3/4"));
        });
    }

    [Test]
    public void Resolve_storyboard_frames_clamps_level_deduplicates_with_half_frame_tolerance_and_keeps_order()
    {
        var scene = new Scene(16, 9, "subdivision") { Duration = TimeSpan.FromSeconds(1) };
        StoryboardShotInput[] nearDuplicateShots =
        [
            new("left", 0),
            new("near-left", 0.010),
            new("right", 1)
        ];

        RenderTools.ResolvedStoryboardFrame[] levelMinus = RenderTools.ResolveStoryboardFrames(scene, nearDuplicateShots, -10).ToArray();
        RenderTools.ResolvedStoryboardFrame[] levelOne = RenderTools.ResolveStoryboardFrames(scene, nearDuplicateShots, 1).ToArray();
        RenderTools.ResolvedStoryboardFrame[] levelLarge = RenderTools.ResolveStoryboardFrames(scene, [new("left", 0), new("right", 1)], 99).ToArray();

        Assert.Multiple(() =>
        {
            AssertTimes(levelMinus, [0, 0.010, 1]);
            Assert.That(levelMinus.Select(frame => frame.Name), Is.EqualTo(new[] { "left", "near-left", "right" }));
            AssertTimes(levelOne, [0, 0.5, 1]);
            Assert.That(levelOne.Select(frame => frame.Name), Is.EqualTo(new[]
            {
                "left",
                "between:left~right@L1:1/2",
                "right"
            }));
            AssertTimes(levelLarge, [0, 0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875, 1]);
            Assert.That(levelLarge.Max(frame => frame.SubdivisionLevel), Is.EqualTo(3));
        });
    }

    [Test]
    public void Resolve_storyboard_frames_subdivides_auto_derived_element_midpoints()
    {
        string workspace = CreateWorkspace();
        Scene scene = CreateStaticStoryboardScene(workspace);

        RenderTools.ResolvedStoryboardFrame[] frames = RenderTools.ResolveStoryboardFrames(scene, null, 1).ToArray();

        Assert.Multiple(() =>
        {
            AssertTimes(frames, [0.5, 1.0, 1.5, 2.0, 2.5]);
            Assert.That(frames.Select(frame => frame.Name), Is.EqualTo(new[]
            {
                "opening",
                "between:opening~middle@L1:1/2",
                "middle",
                "between:middle~closing@L1:1/2",
                "closing"
            }));
            Assert.That(frames.Select(frame => frame.Kind), Is.EqualTo(new[]
            {
                "shot",
                "inbetween",
                "shot",
                "inbetween",
                "shot"
            }));
        });
    }

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
            Assert.That(result.Value.Result.Shots.Select(shot => shot.Kind), Has.All.EqualTo("shot"));
            Assert.That(result.Value.Result.Shots.Select(shot => shot.SubdivisionLevel), Has.All.EqualTo(0));
            Assert.That(File.Exists(result.Value.Result.ContactSheetPath), Is.True);
            Assert.That(result.Value.Result.Shots.Select(shot => shot.StillPath), Has.All.Matches<string>(File.Exists));
            Assert.That(result.Value.Result.Shots.Select(shot => shot.VisibilityAnalysis), Has.All.Not.Null);
            Assert.That(typeof(RenderStoryboardResponse).GetProperties().Select(property => property.Name), Has.None.Contains("Motion"));
            Assert.That(result.Error, Is.Null);
        });
    }

    [Test]
    public async Task Render_storyboard_cut_eye_trace_flags_corner_to_corner_jump()
    {
        string workspace = CreateWorkspace();
        Scene scene = CreateEyeTraceScene(workspace, jumpAcrossCut: true);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(workspace, session);

        CallToolResult call = await tools.RenderStoryboard(
            [
                new StoryboardShotInput("left-focal", 0.5),
                new StoryboardShotInput("right-focal", 1.5)
            ],
            outputDirectory: "storyboards",
            basename: "eye-trace-jump",
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ReadToolResult<RenderStoryboardResult>(call);

        CutEyeTrace trace = result.Value!.Result!.CutEyeTrace.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(trace.LeftFrame, Is.EqualTo("left-focal"));
            Assert.That(trace.RightFrame, Is.EqualTo("right-focal"));
            Assert.That(trace.ExceedsEyeTraceBudget, Is.True);
            Assert.That(trace.DisplacementRatio, Is.GreaterThan(0.33));
            Assert.That(trace.LeftFocalPoint.X, Is.LessThan(0.30));
            Assert.That(trace.LeftFocalPoint.Y, Is.LessThan(0.30));
            Assert.That(trace.RightFocalPoint.X, Is.GreaterThan(0.70));
            Assert.That(trace.RightFocalPoint.Y, Is.GreaterThan(0.70));
            Assert.That(result.Value.Result.ReviewNotes, Has.Some.Contains("Murch"));
        });
    }

    [Test]
    public async Task Render_storyboard_cut_eye_trace_allows_aligned_focal_points()
    {
        string workspace = CreateWorkspace();
        Scene scene = CreateEyeTraceScene(workspace, jumpAcrossCut: false);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(workspace, session);

        CallToolResult call = await tools.RenderStoryboard(
            [
                new StoryboardShotInput("left-focal", 0.5),
                new StoryboardShotInput("aligned-focal", 1.5)
            ],
            outputDirectory: "storyboards",
            basename: "eye-trace-aligned",
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ReadToolResult<RenderStoryboardResult>(call);

        CutEyeTrace trace = result.Value!.Result!.CutEyeTrace.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(trace.ExceedsEyeTraceBudget, Is.False);
            Assert.That(trace.DisplacementRatio, Is.LessThanOrEqualTo(0.33));
            Assert.That(result.Value.Result.ReviewNotes, Is.Empty);
        });
    }

    [Test]
    public async Task Render_storyboard_single_shot_returns_empty_cut_eye_trace()
    {
        string workspace = CreateWorkspace();
        Scene scene = CreateEyeTraceScene(workspace, jumpAcrossCut: true);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(workspace, session);

        CallToolResult call = await tools.RenderStoryboard(
            [new StoryboardShotInput("only-shot", 0.5)],
            outputDirectory: "storyboards",
            basename: "eye-trace-single",
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ReadToolResult<RenderStoryboardResult>(call);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.Result!.CutEyeTrace, Is.Empty);
            Assert.That(result.Value.Result.ReviewNotes, Is.Empty);
        });
    }

    [Test]
    public async Task Render_storyboard_subdivision_response_contains_inbetween_shape_and_deterministic_names()
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
            basename: "subdivided",
            subdivisionLevel: 2,
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ReadToolResult<RenderStoryboardResult>(call);

        RenderStoryboardShot[] renderedShots = result.Value!.Result!.Shots.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(renderedShots.Select(shot => shot.TimeSeconds), Is.EqualTo(new[] { 0.5, 0.75, 1.0, 1.25, 1.5 }).Within(0.000001));
            Assert.That(renderedShots.Select(shot => shot.Kind), Is.EqualTo(new[]
            {
                "shot",
                "inbetween",
                "inbetween",
                "inbetween",
                "shot"
            }));
            Assert.That(renderedShots.Select(shot => shot.SubdivisionLevel), Is.EqualTo(new[] { 0, 2, 1, 2, 0 }));
            Assert.That(renderedShots.Select(shot => shot.Name), Is.EqualTo(new[]
            {
                "opening",
                "between:opening~detail@L2:1/4",
                "between:opening~detail@L1:1/2",
                "between:opening~detail@L2:3/4",
                "detail"
            }));
            Assert.That(renderedShots.Select(shot => shot.StillPath), Has.All.Matches<string>(File.Exists));
            Assert.That(File.Exists(result.Value.Result.ContactSheetPath), Is.True);
        });
    }

    [Test]
    public async Task Render_storyboard_rejects_requests_above_frame_count_cap()
    {
        string workspace = CreateWorkspace();
        var scene = new Scene(16, 9, "too-many") { Duration = TimeSpan.FromSeconds(60) };
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(workspace, session);

        StoryboardShotInput[] shots = Enumerable.Range(0, 49)
            .Select(index => new StoryboardShotInput($"shot-{index}", index))
            .ToArray();
        CallToolResult call = await tools.RenderStoryboard(
            shots,
            outputDirectory: "storyboards",
            basename: "too-many",
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ReadToolResult<RenderStoryboardResult>(call);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Target, Is.EqualTo("subdivisionLevel"));
            Assert.That(result.Error.Message, Does.Contain("48"));
            Assert.That(result.Error.Message, Does.Contain("Lower subdivisionLevel"));
            Assert.That(Directory.Exists(Path.Combine(workspace, "storyboards")), Is.False);
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
            Assert.That(result.Value.Result.Shots.Select(shot => shot.Kind), Has.All.EqualTo("shot"));
            Assert.That(result.Value.Result.Shots.Select(shot => shot.SubdivisionLevel), Has.All.EqualTo(0));
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

    [Test]
    public async Task Compare_revisions_reports_introduced_resolved_issues_regression_and_image_pairs()
    {
        string workspace = CreateWorkspace();
        Scene scene = CreateRevisionCompareScene(workspace);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(workspace, session);

        ToolResult<QualityReviewResponse> baseline = await tools.EvaluateEditQuality(
            timeSeconds: [0.5, 1.5],
            sampleCount: 2,
            allowStillness: true,
            paletteRoleColors: CreateRevisionPaletteRolesJson(),
            cancellationToken: CancellationToken.None);
        SetRevisionTextColor(scene, Colors.White);
        AddRevisionAccentFlood(scene, workspace);

        CallToolResult call = await tools.CompareRevisions(returnImageContent: true, cancellationToken: CancellationToken.None);
        ToolResult<CompareRevisionsResponse> result = ReadToolResult<CompareRevisionsResponse>(call);

        Assert.Multiple(() =>
        {
            Assert.That(baseline.IsSuccess, Is.True, baseline.Error?.Message);
            Assert.That(baseline.Value!.Issues, Has.Some.Matches<QualityIssue>(issue => issue.Category == "typographyContrast"));
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.IssuesResolved, Has.Some.Matches<QualityIssue>(issue => issue.Category == "typographyContrast"));
            Assert.That(result.Value.IssuesIntroduced, Has.Some.Matches<QualityIssue>(issue => issue.Category == "paletteBalance"));
            Assert.That(result.Value.Regression, Is.True);
            Assert.That(result.Value.MetricDeltas.Select(delta => delta.Metric), Does.Contain("paletteBalance.roleShare.accent"));
            Assert.That(result.Value.StillPairs, Has.Count.EqualTo(2));
            Assert.That(result.Value.StillPairs.Select(pair => pair.PreviousPath), Has.All.Matches<string>(File.Exists));
            Assert.That(result.Value.StillPairs.Select(pair => pair.CurrentPath), Has.All.Matches<string>(File.Exists));
            Assert.That(call.Content.OfType<ImageContentBlock>().Count(), Is.EqualTo(4));
        });
    }

    [Test]
    public async Task Compare_revisions_returns_typed_error_without_cached_baseline()
    {
        string workspace = CreateWorkspace();
        using var session = new AgentToolkitTestSession(CreateRevisionCompareScene(workspace));
        RenderTools tools = CreateTools(workspace, session);

        CallToolResult call = await tools.CompareRevisions(cancellationToken: CancellationToken.None);
        ToolResult<CompareRevisionsResponse> result = ReadToolResult<CompareRevisionsResponse>(call);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.StaleHandle));
            Assert.That(result.Error.Message, Does.Contain("cached quality baseline"));
        });
    }

    [Test]
    public async Task Compare_revisions_updates_cache_to_current_report_after_compare()
    {
        string workspace = CreateWorkspace();
        Scene scene = CreateRevisionCompareScene(workspace);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(workspace, session);

        ToolResult<QualityReviewResponse> baseline = await tools.EvaluateEditQuality(
            timeSeconds: [0.5, 1.5],
            sampleCount: 2,
            allowStillness: true,
            paletteRoleColors: CreateRevisionPaletteRolesJson(),
            cancellationToken: CancellationToken.None);
        SetRevisionTextColor(scene, Colors.White);
        AddRevisionAccentFlood(scene, workspace);
        ToolResult<CompareRevisionsResponse> first = ReadToolResult<CompareRevisionsResponse>(
            await tools.CompareRevisions(cancellationToken: CancellationToken.None));
        ToolResult<CompareRevisionsResponse> second = ReadToolResult<CompareRevisionsResponse>(
            await tools.CompareRevisions(cancellationToken: CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(baseline.IsSuccess, Is.True, baseline.Error?.Message);
            Assert.That(first.IsSuccess, Is.True, first.Error?.Message);
            Assert.That(first.Value!.IssuesIntroduced, Is.Not.Empty);
            Assert.That(second.IsSuccess, Is.True, second.Error?.Message);
            Assert.That(second.Value!.IssuesIntroduced, Is.Empty);
            Assert.That(second.Value.IssuesResolved, Is.Empty);
            Assert.That(second.Value.Regression, Is.False);
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
            new AudioRhythmAnalyzer(),
            new QualityAnalyzer(motionVariationAnalyzer, stillRenderer),
            new VideoExporter(new EncoderRegistration()),
            new RenderJobManager());
    }

    private static Scene CreateRevisionCompareScene(string workspace)
    {
        var scene = new Scene(160, 90, "revision-compare")
        {
            Duration = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(workspace, "Scene.scene"))
        };
        AddColorRectElement(scene, workspace, "[role:background] bg-base", TimeSpan.Zero, TimeSpan.FromSeconds(2), 160, 90, Color.Parse("#ff102030"));
        var block = new TextBlock
        {
            Name = "Launch",
            Text = { CurrentValue = "Launch" },
            Size = { CurrentValue = 28 },
            Fill = { CurrentValue = new SolidColorBrush(Color.Parse("#ff1a2028")) }
        };
        AddElement(scene, workspace, "revision text", TimeSpan.Zero, TimeSpan.FromSeconds(2), 10, block);
        return scene;
    }

    private static void SetRevisionTextColor(Scene scene, Color color)
    {
        TextBlock text = scene.Children
            .SelectMany(element => element.Objects)
            .OfType<TextBlock>()
            .Single();
        text.Fill.CurrentValue = new SolidColorBrush(color);
    }

    private static void AddRevisionAccentFlood(Scene scene, string workspace)
    {
        var rect = new RectShape
        {
            Name = "[role:accent] flooded accent field",
            Width = { CurrentValue = 130 },
            Height = { CurrentValue = 90 },
            Fill = { CurrentValue = new SolidColorBrush(Color.Parse("#ffff3030")) },
            Transform =
            {
                CurrentValue = new TransformGroup
                {
                    Children = { new TranslateTransform(-15, 0) }
                }
            }
        };
        AddElement(scene, workspace, "accent flood", TimeSpan.Zero, TimeSpan.FromSeconds(2), 5, rect);
    }

    private static PaletteRoleColor[] CreateRevisionPaletteRoles()
    {
        return
        [
            new PaletteRoleColor("bg-base", "#102030"),
            new PaletteRoleColor("accent", "#ff3030")
        ];
    }

    private static JsonElement CreateRevisionPaletteRolesJson()
    {
        return JsonSerializer.SerializeToElement(CreateRevisionPaletteRoles(), s_jsonOptions);
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

    private static Scene CreateEyeTraceScene(string workspace, bool jumpAcrossCut)
    {
        var scene = new Scene(320, 180, "eye-trace")
        {
            Duration = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(workspace, "Scene.scene"))
        };
        AddColorRectElement(scene, workspace, "black background", TimeSpan.Zero, TimeSpan.FromSeconds(2), 320, 180, Colors.Black);
        AddFocalRectElement(scene, workspace, "first focal", TimeSpan.Zero, TimeSpan.FromSeconds(1), -120, -60);
        AddFocalRectElement(
            scene,
            workspace,
            "second focal",
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            jumpAcrossCut ? 120 : -120,
            jumpAcrossCut ? 60 : -60);
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

    private static void AddFocalRectElement(
        Scene scene,
        string workspace,
        string name,
        TimeSpan start,
        TimeSpan length,
        float x,
        float y)
    {
        var rect = new RectShape
        {
            Name = name,
            Width = { CurrentValue = 48 },
            Height = { CurrentValue = 48 },
            Fill = { CurrentValue = new SolidColorBrush(Colors.White) },
            Transform =
            {
                CurrentValue = new TransformGroup
                {
                    Children = { new TranslateTransform(x, y) }
                }
            }
        };
        AddElement(scene, workspace, name, start, length, 10, rect);
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

    private static void AssertTimes(
        IReadOnlyList<RenderTools.ResolvedStoryboardFrame> frames,
        IReadOnlyList<double> expectedSeconds)
    {
        Assert.That(
            frames.Select(frame => frame.Time.TotalSeconds).ToArray(),
            Is.EqualTo(expectedSeconds.ToArray()).Within(0.000001));
        Assert.That(
            frames.Select(frame => frame.Time.TotalSeconds).SequenceEqual(
                frames.Select(frame => frame.Time.TotalSeconds).OrderBy(time => time)),
            Is.True,
            "storyboard frames must stay chronological");
    }

    private static SKBitmap Decode(ImageContentBlock image)
    {
        return SKBitmap.Decode(image.DecodedData.ToArray())
               ?? throw new InvalidOperationException("Image content block was not a decodable PNG.");
    }
}
