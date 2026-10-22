using System.Globalization;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using ModelContextProtocol.Protocol;
using SkiaSharp;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class RenderToolsStoryboardTests
{
    [Test]
    public void Live_scene_snapshot_is_created_on_session_dispatcher()
    {
        var scene = new Scene(320, 180, "live")
        {
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.scene")),
            Duration = MediaTime.FromSeconds(2)
        };
        using var session = new DispatchingTestSession(scene, rootOnlyInsideDispatch: true);

        Assert.Throws<InvalidOperationException>(() => _ = session.Root);
        Scene snapshot = RenderTools.CreateSceneSnapshot(session);

        Assert.Multiple(() =>
        {
            Assert.That(session.InvokeCount, Is.EqualTo(1));
            Assert.That(snapshot, Is.Not.SameAs(scene));
            Assert.That(snapshot.FrameSize, Is.EqualTo(scene.FrameSize));
            Assert.That(snapshot.Duration, Is.EqualTo(scene.Duration));
            Assert.That(snapshot.Uri, Is.EqualTo(scene.Uri));
        });
    }

    [Test]
    public void Derived_storyboard_shots_normalize_element_midpoints_to_scene_relative_times()
    {
        var scene = new Scene(16, 9, "trimmed")
        {
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.scene")),
            Start = MediaTime.FromSeconds(5),
            Duration = MediaTime.FromSeconds(10)
        };
        scene.Children.Add(new Element
        {
            Name = "before-trim",
            Start = MediaTime.Zero,
            Length = MediaTime.FromSeconds(2),
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"))
        });
        scene.Children.Add(new Element
        {
            Name = "clip",
            Start = MediaTime.FromSeconds(5),
            Length = MediaTime.FromSeconds(10),
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"))
        });

        RenderTools.ResolvedStoryboardFrame[] frames =
            RenderTools.ResolveStoryboardFrames(scene, shots: null, timeSeconds: null, subdivisionLevel: 0).ToArray();

        Assert.Multiple(() =>
        {
            // "before-trim" sits wholly before the visible window, so it derives no shot; "clip"
            // midpoint (absolute 10s) becomes scene-relative 5s, not the double-offset 10s.
            Assert.That(frames.Select(frame => frame.Name), Is.EqualTo(new[] { "clip" }));
            Assert.That(frames.Select(frame => frame.Time),
                Is.EqualTo(new[] { MediaTime.FromSeconds(5) }));
        });
    }

    [Test]
    public void Derived_storyboard_shots_skip_disabled_and_off_window_elements()
    {
        var scene = new Scene(16, 9, "filtered")
        {
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.scene")),
            Duration = MediaTime.FromSeconds(10)
        };
        scene.Children.Add(new Element
        {
            Name = "visible",
            Start = MediaTime.FromSeconds(2),
            Length = MediaTime.FromSeconds(2),
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"))
        });
        scene.Children.Add(new Element
        {
            Name = "disabled",
            IsEnabled = false,
            Start = MediaTime.FromSeconds(4),
            Length = MediaTime.FromSeconds(2),
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"))
        });
        scene.Children.Add(new Element
        {
            Name = "after-window",
            Start = MediaTime.FromSeconds(12),
            Length = MediaTime.FromSeconds(2),
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"))
        });
        scene.Children.Add(new Element
        {
            Name = "zero-length",
            Start = MediaTime.FromSeconds(5),
            Length = MediaTime.Zero,
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"))
        });

        RenderTools.ResolvedStoryboardFrame[] frames =
            RenderTools.ResolveStoryboardFrames(scene, shots: null, timeSeconds: null, subdivisionLevel: 0).ToArray();

        // Only the enabled, in-window, non-empty element contributes a derived shot; the others would
        // add blank frames and burn the subdivision cap.
        Assert.That(frames.Select(frame => frame.Name), Is.EqualTo(new[] { "visible" }));
    }

    [Test]
    public void Explicit_storyboard_shots_at_the_scene_end_clamp_to_the_last_renderable_flick()
    {
        var scene = new Scene(16, 9, "shot-end") { Duration = MediaTime.FromSeconds(4) };

        RenderTools.ResolvedStoryboardFrame[] fromShots =
            RenderTools.ResolveStoryboardFrames(scene, [new("end", 4.0)], timeSeconds: null, subdivisionLevel: 0).ToArray();
        RenderTools.ResolvedStoryboardFrame[] fromTimes =
            RenderTools.ResolveStoryboardFrames(scene, shots: null, timeSeconds: [4.0], subdivisionLevel: 0).ToArray();

        Assert.Multiple(() =>
        {
            // A shot at exactly Duration renders past every element (Range end is exclusive); both the
            // explicit shots and explicit timeSeconds branches must pull it one tick inside.
            Assert.That(fromShots[^1].Time, Is.EqualTo(scene.Duration - new MediaTime(1)));
            Assert.That(fromTimes[^1].Time, Is.EqualTo(scene.Duration - new MediaTime(1)));
        });
    }

    [Test]
    public void Explicit_shots_reject_out_of_range_non_finite_and_duplicate_times()
    {
        var scene = new Scene(16, 9, "validate") { Duration = MediaTime.FromSeconds(5) };

        Assert.Multiple(() =>
        {
            Assert.That(
                Assert.Throws<ReconcileException>(() =>
                    RenderTools.ResolveStoryboardFrames(scene, [new("late", 30)], timeSeconds: null, 0))!.Error.Code,
                Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(
                Assert.Throws<ReconcileException>(() =>
                    RenderTools.ResolveStoryboardFrames(scene, [new("neg", -1)], timeSeconds: null, 0))!.Error.Code,
                Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(
                Assert.Throws<ReconcileException>(() =>
                    RenderTools.ResolveStoryboardFrames(scene, [new("nan", double.NaN)], timeSeconds: null, 0))!.Error.Code,
                Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(
                Assert.Throws<ReconcileException>(() =>
                    RenderTools.ResolveStoryboardFrames(scene, [new("a", 2), new("b", 2)], timeSeconds: null, 0))!.Error.Code,
                Is.EqualTo(ErrorCode.ValidationRejected));
        });
    }

    [Test]
    public void Resolve_storyboard_frames_subdivides_explicit_shots_at_binary_points()
    {
        var scene = new Scene(16, 9, "subdivision") { Duration = MediaTime.FromSeconds(1) };
        StoryboardShotInput[] shots =
        [
            new("left", 0),
            new("right", 1)
        ];

        RenderTools.ResolvedStoryboardFrame[] level1 = RenderTools.ResolveStoryboardFrames(scene, shots, timeSeconds: null, 1).ToArray();
        RenderTools.ResolvedStoryboardFrame[] level2 = RenderTools.ResolveStoryboardFrames(scene, shots, timeSeconds: null, 2).ToArray();
        RenderTools.ResolvedStoryboardFrame[] level3 = RenderTools.ResolveStoryboardFrames(scene, shots, timeSeconds: null, 3).ToArray();

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
        var scene = new Scene(16, 9, "subdivision") { Duration = MediaTime.FromSeconds(1) };
        StoryboardShotInput[] nearDuplicateShots =
        [
            new("left", 0),
            new("near-left", 0.010),
            new("right", 1)
        ];

        RenderTools.ResolvedStoryboardFrame[] levelMinus = RenderTools.ResolveStoryboardFrames(scene, nearDuplicateShots, timeSeconds: null, -10).ToArray();
        RenderTools.ResolvedStoryboardFrame[] levelOne = RenderTools.ResolveStoryboardFrames(scene, nearDuplicateShots, timeSeconds: null, 1).ToArray();
        RenderTools.ResolvedStoryboardFrame[] levelLarge = RenderTools.ResolveStoryboardFrames(scene, [new("left", 0), new("right", 1)], timeSeconds: null, 99).ToArray();

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
    public void Resolve_storyboard_frames_uses_the_project_frame_rate_for_dedupe_tolerance()
    {
        StoryboardShotInput[] nearDuplicateShots =
        [
            new("left", 0),
            new("near-left", 0.010),
            new("right", 1)
        ];

        // At 30 fps the half-frame tolerance is ~16.7 ms, so the 10 ms-apart anchor is deduped away;
        // at 60 fps it halves to ~8.3 ms, so the same anchor survives.
        RenderTools.ResolvedStoryboardFrame[] at30 = RenderTools
            .ResolveStoryboardFrames(CreateScene(30), nearDuplicateShots, timeSeconds: null, subdivisionLevel: 1)
            .ToArray();
        RenderTools.ResolvedStoryboardFrame[] at60 = RenderTools
            .ResolveStoryboardFrames(CreateScene(60), nearDuplicateShots, timeSeconds: null, subdivisionLevel: 1)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(at30.Select(frame => frame.Name), Does.Not.Contain("near-left"));
            Assert.That(at60.Select(frame => frame.Name), Does.Contain("near-left"));
        });

        static Scene CreateScene(int frameRate)
        {
            var scene = new Scene(16, 9, "frame-rate") { Duration = MediaTime.FromSeconds(1) };
            var project = new Project();
            project.Variables[ProjectVariableKeys.FrameRate] = frameRate.ToString(CultureInfo.InvariantCulture);
            project.Items.Add(scene);
            return scene;
        }
    }

    [Test]
    public void Resolve_frame_sample_times_rejects_an_explicit_list_with_fewer_than_two_distinct_times()
    {
        var scene = new Scene(16, 9, "quality") { Duration = MediaTime.FromSeconds(4) };

        Assert.Multiple(() =>
        {
            Assert.That(() => RenderTools.ResolveFrameSampleTimes(scene, [1.0], sampleCount: 4),
                Throws.TypeOf<ReconcileException>());
            Assert.That(() => RenderTools.ResolveFrameSampleTimes(scene, [1.0, 1.0], sampleCount: 4),
                Throws.TypeOf<ReconcileException>());
            // An omitted list still falls back to auto-generated samples.
            Assert.That(RenderTools.ResolveFrameSampleTimes(scene, null, sampleCount: 4), Has.Count.EqualTo(4));
        });
    }

    [Test]
    public void Resolve_storyboard_frames_subdivides_auto_derived_element_midpoints()
    {
        string workspace = TestWorkspace.CreateDirectory();
        Scene scene = CreateStaticStoryboardScene(workspace);

        RenderTools.ResolvedStoryboardFrame[] frames = RenderTools.ResolveStoryboardFrames(scene, null, timeSeconds: null, 1).ToArray();

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
    public void Resolve_storyboard_frames_time_seconds_override_auto_detection_and_subdivide()
    {
        string workspace = TestWorkspace.CreateDirectory();
        var scene = new Scene(320, 180, "single-shot")
        {
            Duration = MediaTime.FromSeconds(6),
            Uri = new Uri(Path.Combine(workspace, "Scene.scene"))
        };
        AddColorRectElement(scene, workspace, "continuous", MediaTime.Zero, MediaTime.FromSeconds(6), 320, 180, Colors.Red);

        RenderTools.ResolvedStoryboardFrame[] frames = RenderTools.ResolveStoryboardFrames(
            scene,
            shots: null,
            timeSeconds: [0.2, 2.7, 5.9],
            subdivisionLevel: 1).ToArray();

        Assert.Multiple(() =>
        {
            AssertTimes(frames, [0.2, 1.45, 2.7, 4.3, 5.9]);
            Assert.That(frames.Select(frame => frame.Name), Is.EqualTo(new[]
            {
                "t:0.2",
                "between:t:0.2~t:2.7@L1:1/2",
                "t:2.7",
                "between:t:2.7~t:5.9@L1:1/2",
                "t:5.9"
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

    [TestCaseSource(nameof(InvalidStoryboardTimeCases))]
    public async Task Render_storyboard_rejects_invalid_time_seconds(double[] timeSeconds, string expectedMessage)
    {
        string workspace = TestWorkspace.CreateDirectory();
        Scene scene = CreateStaticStoryboardScene(workspace);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(session);

        CallToolResult call = await tools.RenderStoryboard(
            timeSeconds: timeSeconds,
            outputDirectory: Path.Combine(workspace, "storyboards"),
            basename: "invalid-times",
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ToolResultReader.Read<RenderStoryboardResult>(call);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Target, Is.EqualTo("timeSeconds"));
            Assert.That(result.Error.Message, Does.Contain(expectedMessage));
            Assert.That(Directory.Exists(Path.Combine(workspace, "storyboards")), Is.False);
        });
    }

    [Test]
    public async Task Render_storyboard_rejects_time_seconds_above_frame_count_cap()
    {
        string workspace = TestWorkspace.CreateDirectory();
        var scene = new Scene(16, 9, "too-many-times") { Duration = MediaTime.FromSeconds(60) };
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(session);

        double[] timeSeconds = Enumerable.Range(0, 49).Select(index => (double)index).ToArray();
        CallToolResult call = await tools.RenderStoryboard(
            timeSeconds: timeSeconds,
            outputDirectory: Path.Combine(workspace, "storyboards"),
            basename: "too-many-times",
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ToolResultReader.Read<RenderStoryboardResult>(call);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Target, Is.EqualTo("timeSeconds"));
            Assert.That(result.Error.Message, Does.Contain("48"));
            Assert.That(result.Error.Hint, Does.Contain("fewer timeSeconds"));
            Assert.That(Directory.Exists(Path.Combine(workspace, "storyboards")), Is.False);
        });
    }

    [Test]
    public async Task Render_storyboard_explicit_shots_writes_contact_sheet_and_visibility_analysis_without_motion_output()
    {
        string workspace = TestWorkspace.CreateDirectory();
        Scene scene = CreateStaticStoryboardScene(workspace);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(session);

        CallToolResult call = await tools.RenderStoryboard(
            shots: [
                new StoryboardShotInput("opening", 0.5),
                new StoryboardShotInput("detail", 1.5)
            ],
            outputDirectory: Path.Combine(workspace, "storyboards"),
            basename: "explicit",
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ToolResultReader.Read<RenderStoryboardResult>(call);

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
        });
    }

    [Test]
    public async Task Render_storyboard_cut_eye_trace_measures_corner_to_corner_displacement()
    {
        string workspace = TestWorkspace.CreateDirectory();
        Scene scene = CreateEyeTraceScene(workspace, jumpAcrossCut: true);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(session);

        CallToolResult call = await tools.RenderStoryboard(
            shots: [
                new StoryboardShotInput("left-focal", 0.5),
                new StoryboardShotInput("right-focal", 1.5)
            ],
            outputDirectory: Path.Combine(workspace, "storyboards"),
            basename: "eye-trace-jump",
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ToolResultReader.Read<RenderStoryboardResult>(call);

        CutEyeTrace trace = result.Value!.Result!.CutEyeTrace.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(trace.LeftFrame, Is.EqualTo("left-focal"));
            Assert.That(trace.RightFrame, Is.EqualTo("right-focal"));
            Assert.That(trace.DisplacementRatio, Is.GreaterThan(0.33));
            Assert.That(trace.LeftFocalPoint.X, Is.LessThan(0.30));
            Assert.That(trace.LeftFocalPoint.Y, Is.LessThan(0.30));
            Assert.That(trace.RightFocalPoint.X, Is.GreaterThan(0.70));
            Assert.That(trace.RightFocalPoint.Y, Is.GreaterThan(0.70));
        });
    }

    [Test]
    public async Task Render_storyboard_cut_eye_trace_measures_aligned_focal_points()
    {
        string workspace = TestWorkspace.CreateDirectory();
        Scene scene = CreateEyeTraceScene(workspace, jumpAcrossCut: false);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(session);

        CallToolResult call = await tools.RenderStoryboard(
            shots: [
                new StoryboardShotInput("left-focal", 0.5),
                new StoryboardShotInput("aligned-focal", 1.5)
            ],
            outputDirectory: Path.Combine(workspace, "storyboards"),
            basename: "eye-trace-aligned",
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ToolResultReader.Read<RenderStoryboardResult>(call);

        CutEyeTrace trace = result.Value!.Result!.CutEyeTrace.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(trace.DisplacementRatio, Is.LessThanOrEqualTo(0.33));
        });
    }

    [Test]
    public async Task Render_storyboard_single_shot_returns_empty_cut_eye_trace()
    {
        string workspace = TestWorkspace.CreateDirectory();
        Scene scene = CreateEyeTraceScene(workspace, jumpAcrossCut: true);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(session);

        CallToolResult call = await tools.RenderStoryboard(
            shots: [new StoryboardShotInput("only-shot", 0.5)],
            outputDirectory: Path.Combine(workspace, "storyboards"),
            basename: "eye-trace-single",
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ToolResultReader.Read<RenderStoryboardResult>(call);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.Result!.CutEyeTrace, Is.Empty);
        });
    }

    [Test]
    public async Task Render_storyboard_subdivision_response_contains_inbetween_shape_and_deterministic_names()
    {
        string workspace = TestWorkspace.CreateDirectory();
        Scene scene = CreateStaticStoryboardScene(workspace);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(session);

        CallToolResult call = await tools.RenderStoryboard(
            shots: [
                new StoryboardShotInput("opening", 0.5),
                new StoryboardShotInput("detail", 1.5)
            ],
            outputDirectory: Path.Combine(workspace, "storyboards"),
            basename: "subdivided",
            subdivisionLevel: 2,
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ToolResultReader.Read<RenderStoryboardResult>(call);

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
        string workspace = TestWorkspace.CreateDirectory();
        var scene = new Scene(16, 9, "too-many") { Duration = MediaTime.FromSeconds(60) };
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(session);

        StoryboardShotInput[] shots = Enumerable.Range(0, 49)
            .Select(index => new StoryboardShotInput($"shot-{index}", index))
            .ToArray();
        CallToolResult call = await tools.RenderStoryboard(
            shots: shots,
            outputDirectory: Path.Combine(workspace, "storyboards"),
            basename: "too-many",
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ToolResultReader.Read<RenderStoryboardResult>(call);

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
        string workspace = TestWorkspace.CreateDirectory();
        Scene scene = CreateStaticStoryboardScene(workspace);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(session);

        CallToolResult call = await tools.RenderStoryboard(
            outputDirectory: Path.Combine(workspace, "storyboards"),
            basename: "auto",
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ToolResultReader.Read<RenderStoryboardResult>(call);

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
    public async Task Render_storyboard_default_basename_includes_session_id_to_avoid_collisions()
    {
        string workspace = TestWorkspace.CreateDirectory();
        Scene firstScene = CreateStaticStoryboardScene(workspace);
        Scene secondScene = CreateStaticStoryboardScene(workspace);
        using var firstSession = new AgentToolkitTestSession(firstScene);
        using var secondSession = new AgentToolkitTestSession(secondScene);
        RenderTools firstTools = CreateTools(firstSession);
        RenderTools secondTools = CreateTools(secondSession);

        ToolResult<RenderStoryboardResult> first = ToolResultReader.Read<RenderStoryboardResult>(
            await firstTools.RenderStoryboard(workspace, cancellationToken: CancellationToken.None));
        ToolResult<RenderStoryboardResult> second = ToolResultReader.Read<RenderStoryboardResult>(
            await secondTools.RenderStoryboard(workspace, cancellationToken: CancellationToken.None));

        string firstFile = Path.GetFileName(first.Value!.Result!.ContactSheetPath);
        string secondFile = Path.GetFileName(second.Value!.Result!.ContactSheetPath);
        Assert.Multiple(() =>
        {
            Assert.That(first.IsSuccess, Is.True, first.Error?.Message);
            Assert.That(second.IsSuccess, Is.True, second.Error?.Message);
            Assert.That(first.Value.Result.ContactSheetPath, Is.Not.EqualTo(second.Value.Result.ContactSheetPath));
            Assert.That(firstFile, Does.Contain(firstSession.SessionId));
            Assert.That(secondFile, Does.Contain(secondSession.SessionId));
            Assert.That(firstFile, Is.Not.EqualTo("storyboard-contact-sheet.png"));
            Assert.That(secondFile, Is.Not.EqualTo("storyboard-contact-sheet.png"));
        });
    }

    [Test]
    public async Task Render_storyboard_background_returns_running_job_then_completes()
    {
        TestGpu.RequireAnyDevice();

        string workspace = TestWorkspace.CreateDirectory();
        Scene scene = CreateStaticStoryboardScene(workspace);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(session);

        CallToolResult call = await tools.RenderStoryboard(
            outputDirectory: Path.Combine(workspace, "storyboards"),
            basename: "bg",
            background: true,
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> started = ToolResultReader.Read<RenderStoryboardResult>(call);

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
        string workspace = TestWorkspace.CreateDirectory();
        Scene scene = CreateStaticStoryboardScene(workspace);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(session);

        CallToolResult call = await tools.RenderStoryboard(
            shots: [
                new StoryboardShotInput("opening", 0.5),
                new StoryboardShotInput("detail", 1.5),
                new StoryboardShotInput("closing", 2.5)
            ],
            outputDirectory: Path.Combine(workspace, "storyboards"),
            basename: "with-image",
            returnImageContent: true,
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ToolResultReader.Read<RenderStoryboardResult>(call);
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
        string workspace = TestWorkspace.CreateDirectory();
        var scene = new Scene(1600, 900, "large-still")
        {
            Duration = MediaTime.FromSeconds(1),
            Uri = new Uri(Path.Combine(workspace, "Scene.scene"))
        };
        AddColorRectElement(scene, workspace, "large background", MediaTime.Zero, MediaTime.FromSeconds(1), 1600, 900, Colors.Red);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(session);

        CallToolResult call = await tools.RenderStill(
            Path.Combine(workspace, "large-still.png"),
            returnImageContent: true,
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStillResponse> result = ToolResultReader.Read<RenderStillResponse>(call);
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

    [TestCase("render_still")]
    [TestCase("export_video")]
    [TestCase("render_storyboard")]
    [TestCase("measure_frame_differences")]
    public async Task Every_render_scale_tool_rejects_over_limit_extent_before_rendering(string toolName)
    {
        string workspace = TestWorkspace.CreateDirectory();
        var scene = new Scene(1920, 1080, $"oversized-{toolName}")
        {
            Duration = MediaTime.FromSeconds(1),
            Uri = new Uri(Path.Combine(workspace, "Scene.scene"))
        };
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(session);
        // The overwrite guard, and for .webm the encoder preflight, would reject first if the extent check ran late.
        string existingOutput = Path.Combine(workspace, toolName == "export_video" ? "oversized.webm" : "oversized.png");
        File.WriteAllText(existingOutput, "existing output");

        ToolError? error;
        switch (toolName)
        {
            case "render_still":
                error = ToolResultReader.Read<RenderStillResponse>(
                    await tools.RenderStill(
                        existingOutput,
                        renderScale: 9f,
                        confirmOverwrite: false,
                        cancellationToken: CancellationToken.None)).Error;
                break;
            case "export_video":
                error = (await tools.ExportVideo(
                    existingOutput,
                    renderScale: 9f,
                    confirmOverwrite: false,
                    cancellationToken: CancellationToken.None)).Error;
                break;
            case "render_storyboard":
                error = ToolResultReader.Read<RenderStoryboardResult>(
                    await tools.RenderStoryboard(
                        timeSeconds: [0],
                        outputDirectory: Path.Combine(workspace, "oversized-storyboard"),
                        renderScale: 9,
                        cancellationToken: CancellationToken.None)).Error;
                break;
            case "measure_frame_differences":
                error = (await tools.MeasureFrameDifferences(
                    timeSeconds: [0, 0.5],
                    renderScale: 9,
                    cancellationToken: CancellationToken.None)).Error;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(toolName), toolName, null);
        }

        AssertRenderScaleLimitError(error, "17280x9720");
    }

    [Test]
    public async Task Render_storyboard_rejects_image_content_for_background_job()
    {
        string workspace = TestWorkspace.CreateDirectory();
        Scene scene = CreateStaticStoryboardScene(workspace);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(session);

        CallToolResult call = await tools.RenderStoryboard(
            outputDirectory: Path.Combine(workspace, "storyboards"),
            basename: "bg-with-image",
            background: true,
            returnImageContent: true,
            cancellationToken: CancellationToken.None);
        ToolResult<RenderStoryboardResult> result = ToolResultReader.Read<RenderStoryboardResult>(call);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(call.Content.OfType<ImageContentBlock>(), Is.Empty);
        });
    }

    [Test]
    public async Task Busy_output_operation_is_rejected_before_session_capture()
    {
        string workspace = TestWorkspace.CreateDirectory();
        var outputOperations = new RejectingOutputOperationLeaseProvider();
        RenderTools tools = TestRenderTools.Create(new AgentSessionManager(), outputOperations: outputOperations);

        ToolResult<RenderStillResponse> still = ToolResultReader.Read<RenderStillResponse>(
            await tools.RenderStill(Path.Combine(workspace, "busy-still.png"), cancellationToken: CancellationToken.None));
        ToolResult<RenderStoryboardResult> storyboard = ToolResultReader.Read<RenderStoryboardResult>(
            await tools.RenderStoryboard(workspace, background: true, cancellationToken: CancellationToken.None));
        ToolResult<FrameDifferenceResponse> motion = await tools.MeasureFrameDifferences(
            cancellationToken: CancellationToken.None);
        ToolResult<ExportVideoResult> export = await tools.ExportVideo(
            Path.Combine(workspace, "busy-export.unknown"),
            background: true,
            cancellationToken: CancellationToken.None);

        Assert.Multiple(() =>
        {
            AssertWorkspaceBusy(still);
            AssertWorkspaceBusy(storyboard);
            AssertWorkspaceBusy(motion);
            AssertWorkspaceBusy(export);
            Assert.That(outputOperations.AcquireAttempts, Is.EqualTo(4));
            Assert.That(Directory.GetFiles(workspace, "*", SearchOption.AllDirectories), Is.Empty);
        });
    }

    [Test]
    public async Task Background_enqueue_failure_releases_the_caller_owned_output_operation_once()
    {
        string workspace = TestWorkspace.CreateDirectory();
        using var session = new AgentToolkitTestSession(CreateStaticScene(workspace));
        var outputOperations = new TrackingOutputOperationLeaseProvider();
        var renderJobs = new RenderJobManager();
        renderJobs.Dispose();
        RenderTools tools = CreateTools(
            session,
            outputOperations,
            renderJobs: renderJobs);

        ToolResult<RenderStoryboardResult> result = ToolResultReader.Read<RenderStoryboardResult>(
            await tools.RenderStoryboard(
                outputDirectory: Path.Combine(workspace, "enqueue-failure"),
                basename: "busy",
                background: true,
                cancellationToken: CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo("internal_error"));
            Assert.That(outputOperations.AcquireAttempts, Is.EqualTo(1));
            Assert.That(outputOperations.Lease, Is.Not.Null);
            Assert.That(outputOperations.Lease!.DisposeCount, Is.EqualTo(1));
            Assert.That(Directory.GetFiles(workspace, "*", SearchOption.AllDirectories), Is.Empty);
        });
    }

    [Test]
    public async Task Background_storyboard_transfers_its_output_operation_to_the_queued_job()
    {
        string workspace = TestWorkspace.CreateDirectory();
        using var session = new AgentToolkitTestSession(CreateStaticScene(workspace));
        var outputOperations = new TrackingOutputOperationLeaseProvider();
        using var renderJobs = new RenderJobManager();
        var blockerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBlocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string blockerJob = renderJobs.Enqueue("blocker", async (_, _) =>
        {
            blockerStarted.TrySetResult();
            await releaseBlocker.Task.ConfigureAwait(false);
            return JsonValue.Create(true)!;
        }, new CountingLease());
        await blockerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        RenderTools tools = CreateTools(
            session,
            outputOperations,
            renderJobs);

        try
        {
            ToolResult<RenderStoryboardResult> started = ToolResultReader.Read<RenderStoryboardResult>(
                await tools.RenderStoryboard(
                    outputDirectory: Path.Combine(workspace, "queued-storyboard"),
                    basename: "queued",
                    background: true,
                    cancellationToken: CancellationToken.None));

            Assert.Multiple(() =>
            {
                Assert.That(started.IsSuccess, Is.True, started.Error?.Message);
                Assert.That(started.Value!.Status, Is.EqualTo("running"));
                Assert.That(outputOperations.Lease, Is.Not.Null);
                Assert.That(outputOperations.Lease!.DisposeCount, Is.EqualTo(0));
            });

            string storyboardJob = started.Value!.JobId!;
            tools.CancelRenderJob(storyboardJob);
            RenderJobSnapshot cancelled = await PollUntilTerminalAsync(tools, storyboardJob);

            Assert.Multiple(() =>
            {
                Assert.That(cancelled.State, Is.EqualTo("cancelled"));
                Assert.That(outputOperations.Lease!.DisposeCount, Is.EqualTo(1));
                Assert.That(Directory.GetFiles(workspace, "*", SearchOption.AllDirectories), Is.Empty);
            });
        }
        finally
        {
            releaseBlocker.TrySetResult();
            await PollUntilTerminalAsync(tools, blockerJob);
        }
    }

    [Test]
    public async Task Render_still_holds_output_operation_until_the_file_is_complete()
    {
        string workspace = TestWorkspace.CreateDirectory();
        string expectedPath = Path.Combine(workspace, "leased-still.png");
        using var session = new AgentToolkitTestSession(CreateStaticScene(workspace));
        var outputOperations = new CompletionObservingOutputOperationLeaseProvider(
            () => File.Exists(expectedPath) && new FileInfo(expectedPath).Length > 0);
        RenderTools tools = CreateTools(session, outputOperations);

        ToolResult<RenderStillResponse> result = ToolResultReader.Read<RenderStillResponse>(
            await tools.RenderStill(expectedPath, cancellationToken: CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(outputOperations.AcquireAttempts, Is.EqualTo(1));
            Assert.That(outputOperations.FileWasCompleteWhenReleased, Is.True);
            Assert.That(result.Value!.OutputPath, Is.EqualTo(expectedPath));
            Assert.That(Directory.GetDirectories(workspace), Is.Empty, "Rendering a still must not create an output directory.");
        });
    }

    private static IEnumerable<TestCaseData> InvalidStoryboardTimeCases()
    {
        yield return new TestCaseData(Array.Empty<double>(), "at least one");
        yield return new TestCaseData(new[] { double.NaN }, "finite");
        yield return new TestCaseData(new[] { -0.1 }, "outside the scene range");
        yield return new TestCaseData(new[] { 3.1 }, "outside the scene range");
        yield return new TestCaseData(new[] { 1.0, 1.0 }, "duplicate");
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
    public void Resolve_frame_sample_times_clamp_the_exclusive_scene_end_to_the_last_renderable_flick()
    {
        var scene = new Scene(320, 180, "clamp-edge") { Duration = MediaTime.FromSeconds(2) };

        IReadOnlyList<MediaTime> samples = RenderTools.ResolveFrameSampleTimes(
            scene, [0.0, scene.Duration.ToSeconds()], sampleCount: 2);

        Assert.That(samples, Is.EqualTo(new[] { MediaTime.Zero, scene.Duration - new MediaTime(1) }));
    }

    [Test]
    public async Task Frame_measurement_rejects_times_that_all_clamp_to_a_single_sample()
    {
        string workspace = TestWorkspace.CreateDirectory();
        var scene = new Scene(320, 180, "clamp-collapse")
        {
            Duration = MediaTime.FromSeconds(2),
            Uri = new Uri(Path.Combine(workspace, "Scene.scene"))
        };
        AddColorRectElement(scene, workspace, "bg", MediaTime.Zero, MediaTime.FromSeconds(2), 320, 180, Colors.Red);
        using var session = new AgentToolkitTestSession(scene);
        RenderTools tools = CreateTools(session);

        // Both times sit past the 2s duration, so each clamps to 2s and Distinct() collapses to one
        // sample — which must surface as a validation error, not an unmapped analyzer exception.
        ToolResult<FrameDifferenceResponse> result = await tools.MeasureFrameDifferences(
            timeSeconds: [5.0, 6.0],
            sampleCount: 2,
            cancellationToken: CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Target, Is.EqualTo("timeSeconds"));
        });
    }

    [Test]
    public async Task Export_video_rejects_a_relative_path_before_the_encoder_preflight()
    {
        string workspace = TestWorkspace.CreateDirectory();
        using var session = new AgentToolkitTestSession(CreateStaticScene(workspace));
        RenderTools tools = CreateTools(session);

        // .webm needs the FFmpeg worker, which a test host may lack; the path is still checked first.
        ToolResult<ExportVideoResult> result = await tools.ExportVideo(
            "movie.webm",
            cancellationToken: CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Target, Is.EqualTo("outputPath"));
        });
    }

    [Test]
    public async Task Audio_rhythm_analysis_rejects_a_relative_path()
    {
        RenderTools tools = TestRenderTools.Create(new AgentSessionManager());

        ToolResult<AnalyzeAudioRhythmResponse> result = await tools.AnalyzeAudioRhythm(
            "beat.wav",
            cancellationToken: CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Target, Is.EqualTo("path"));
        });
    }

    private static RenderTools CreateTools(
        AgentToolkitTestSession session,
        IOutputOperationLeaseProvider? outputOperations = null,
        RenderJobManager? renderJobs = null)
        => TestRenderTools.Create(session.CreateManager(), renderJobs: renderJobs, outputOperations: outputOperations);

    private static void AssertWorkspaceBusy<T>(ToolResult<T> result)
    {
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.WorkspaceBusy));
        Assert.That(result.Error.Hint, Does.Contain("Retry"));
    }

    private sealed class RejectingOutputOperationLeaseProvider : IOutputOperationLeaseProvider
    {
        public int AcquireAttempts { get; private set; }

        public IDisposable? TryBeginOutputOperation()
        {
            AcquireAttempts++;
            return null;
        }
    }

    private sealed class CompletionObservingOutputOperationLeaseProvider(Func<bool> isFileComplete)
        : IOutputOperationLeaseProvider
    {
        public int AcquireAttempts { get; private set; }

        public bool FileWasCompleteWhenReleased { get; private set; }

        public IDisposable? TryBeginOutputOperation()
        {
            AcquireAttempts++;
            return new CountingLease(() => FileWasCompleteWhenReleased = isFileComplete());
        }
    }

    private sealed class TrackingOutputOperationLeaseProvider : IOutputOperationLeaseProvider
    {
        public int AcquireAttempts { get; private set; }

        public CountingLease? Lease { get; private set; }

        public IDisposable? TryBeginOutputOperation()
        {
            AcquireAttempts++;
            Lease = new CountingLease();
            return Lease;
        }
    }

    private static Scene CreateStaticStoryboardScene(string workspace)
    {
        var scene = new Scene(320, 180, "storyboard")
        {
            Duration = MediaTime.FromSeconds(3),
            Uri = new Uri(Path.Combine(workspace, "Scene.scene"))
        };
        AddColorRectElement(scene, workspace, "opening", MediaTime.Zero, MediaTime.FromSeconds(1), 320, 180, Colors.Red);
        AddColorRectElement(scene, workspace, "middle", MediaTime.FromSeconds(1), MediaTime.FromSeconds(1), 320, 180, Colors.Blue);
        AddColorRectElement(scene, workspace, "closing", MediaTime.FromSeconds(2), MediaTime.FromSeconds(1), 320, 180, Colors.Green);
        return scene;
    }

    private static Scene CreateStaticScene(string workspace)
    {
        var scene = new Scene(320, 180, "static-scene")
        {
            Duration = MediaTime.FromSeconds(3),
            Uri = new Uri(Path.Combine(workspace, "Scene.scene"))
        };
        AddColorRectElement(scene, workspace, "Background plate", MediaTime.Zero, MediaTime.FromSeconds(3), 320, 180, Color.Parse("#ff20242b"));
        AddColorRoundedRectElement(scene, workspace, "Title backing plate", MediaTime.Zero, MediaTime.FromSeconds(3), 120, 44, Color.Parse("#eeedf0e8"));
        AddTextElement(scene, workspace, "Launch notes", MediaTime.Zero, MediaTime.FromSeconds(3));
        AddEllipseElement(scene, workspace, "Soft accent", MediaTime.Zero, MediaTime.FromSeconds(3));
        return scene;
    }

    private static Scene CreateEyeTraceScene(string workspace, bool jumpAcrossCut)
    {
        var scene = new Scene(320, 180, "eye-trace")
        {
            Duration = MediaTime.FromSeconds(2),
            Uri = new Uri(Path.Combine(workspace, "Scene.scene"))
        };
        AddColorRectElement(scene, workspace, "black background", MediaTime.Zero, MediaTime.FromSeconds(2), 320, 180, Colors.Black);
        AddFocalRectElement(scene, workspace, "first focal", MediaTime.Zero, MediaTime.FromSeconds(1), -120, -60);
        AddFocalRectElement(
            scene,
            workspace,
            "second focal",
            MediaTime.FromSeconds(1),
            MediaTime.FromSeconds(1),
            jumpAcrossCut ? 120 : -120,
            jumpAcrossCut ? 60 : -60);
        return scene;
    }

    private static void AddColorRectElement(
        Scene scene,
        string workspace,
        string name,
        MediaTime start,
        MediaTime length,
        float width,
        float height,
        Color color)
    {
        var rect = new RectShape
        {
            Name = name,
            Width = { Value = width },
            Height = { Value = height },
            Fill = { Value = new SolidColorBrush(color) }
        };
        AddElement(scene, workspace, name, start, length, scene.Children.Count, rect);
    }

    private static void AddFocalRectElement(
        Scene scene,
        string workspace,
        string name,
        MediaTime start,
        MediaTime length,
        float x,
        float y)
    {
        var rect = new RectShape
        {
            Name = name,
            Width = { Value = 48 },
            Height = { Value = 48 },
            Fill = { Value = new SolidColorBrush(Colors.White) },
            Transform =
            {
                Value = new TransformGroup
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
        MediaTime start,
        MediaTime length,
        float width,
        float height,
        Color color)
    {
        var rect = new RoundedRectShape
        {
            Name = name,
            Width = { Value = width },
            Height = { Value = height },
            CornerRadius = { Value = new CornerRadius(12) },
            Fill = { Value = new SolidColorBrush(color) }
        };
        AddElement(scene, workspace, name, start, length, scene.Children.Count, rect);
    }

    private static void AddTextElement(Scene scene, string workspace, string text, MediaTime start, MediaTime length)
    {
        var block = new TextBlock
        {
            Name = text,
            Text = { Value = text },
            Size = { Value = 24 },
            Fill = { Value = new SolidColorBrush(Color.Parse("#ff1a2028")) }
        };
        AddElement(scene, workspace, text, start, length, 10, block);
    }

    private static void AddEllipseElement(Scene scene, string workspace, string name, MediaTime start, MediaTime length)
    {
        var ellipse = new EllipseShape
        {
            Name = name,
            Width = { Value = 42 },
            Height = { Value = 42 },
            Fill = { Value = new SolidColorBrush(Color.Parse("#ffb85c38")) },
            Transform =
            {
                Value = new TransformGroup
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
        MediaTime start,
        MediaTime length,
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

    // 2000 divides 1000 times a power-of-two limit, so the exact limit has three decimals and the hint is the
    // step below it.
    [TestCase(1920, 1080)]
    [TestCase(1080, 1920)]
    [TestCase(2000, 2000)]
    [TestCase(7, 5)]
    public void Render_scale_hint_is_three_decimals_below_the_limit_and_accepted(int width, int height)
    {
        var scene = new Scene(width, height, "hint");
        int limit = BufferDimensionBudget.Resolve(BufferBudgetScope.Prediction).MaxDimension;
        long thousandths = ((1000L * limit) - 1) / Math.Max(width, height);
        string expected = (thousandths / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);

        var rejected = Assert.Throws<ReconcileException>(
            () => RenderTools.ValidateRenderScale(scene, limit + 1f, "render_still"));
        float hinted = float.Parse(expected, CultureInfo.InvariantCulture);

        Assert.Multiple(() =>
        {
            Assert.That(rejected!.Error.Hint, Is.EqualTo($"Use renderScale <= {expected} for this frame size."));
            Assert.That(RenderTools.ValidateRenderScale(scene, hinted, "render_still"), Is.EqualTo(hinted));
        });
    }

    [Test]
    public void Render_scale_hint_for_a_frame_over_a_thousand_times_the_limit_names_no_scale()
    {
        int limit = BufferDimensionBudget.Resolve(BufferBudgetScope.Prediction).MaxDimension;
        var scene = new Scene(1000 * limit + 1, 1, "huge");

        var rejected = Assert.Throws<ReconcileException>(
            () => RenderTools.ValidateRenderScale(scene, 1f, "render_still"));

        Assert.Multiple(() =>
        {
            Assert.That(rejected!.Error.Message, Does.EndWith("needs a renderScale below 0.001."));
            Assert.That(rejected.Error.Hint, Is.EqualTo("Use a renderScale below 0.001 for this frame size."));
        });
    }

    // The limit depends on the device, so it is read here instead of hard-coding 16384.
    private static void AssertRenderScaleLimitError(ToolError? error, string requestedExtent)
    {
        int limit = BufferDimensionBudget.Resolve(BufferBudgetScope.Prediction).MaxDimension;
        string maximumScale = (((1000L * limit) - 1) / 1920 / 1000.0)
            .ToString("0.###", CultureInfo.InvariantCulture);

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(error.Target, Is.EqualTo("renderScale"));
            Assert.That(error.Message, Does.Contain(limit.ToString(CultureInfo.InvariantCulture)));
            Assert.That(error.Message, Does.Contain(requestedExtent));
            Assert.That(error.Message, Does.EndWith($"is {maximumScale}."));
            Assert.That(error.Hint, Is.EqualTo($"Use renderScale <= {maximumScale} for this frame size."));
        });
    }

    private static void AssertTimes(
        IReadOnlyList<RenderTools.ResolvedStoryboardFrame> frames,
        IReadOnlyList<double> expectedSeconds)
    {
        Assert.That(
            frames.Select(frame => frame.Time.ToSeconds()).ToArray(),
            Is.EqualTo(expectedSeconds.ToArray()).Within(0.000001));
    }

    private static SKBitmap Decode(ImageContentBlock image)
    {
        return SKBitmap.Decode(image.DecodedData.ToArray())
               ?? throw new InvalidOperationException("Image content block was not a decodable PNG.");
    }
}
