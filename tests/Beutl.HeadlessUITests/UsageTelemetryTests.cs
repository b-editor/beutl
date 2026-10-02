using System.Diagnostics;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Beutl.Animation;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Components.ColorScopesTab.ViewModels;
using Beutl.Editor.Components.GraphEditorTab.Views;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PathEditorTab.Views;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
using Moq;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class UsageTelemetryTests
{
    private readonly List<UsageSummary> _summaries = [];
    private UsageTelemetry? _previous;
    private UsageTelemetry _usage = null!;
    private TelemetryConfig _config = null!;
    private ManualTime _time = null!;

    [SetUp]
    public void SetUp()
    {
        _summaries.Clear();
        _previous = UsageTelemetry.Current;
        _config = new TelemetryConfig
        {
            Beutl_Application = true,
            Beutl_Api_Client = false,
            Beutl_PackageManagement = false,
            Beutl_Logging = false
        };
        _time = new ManualTime();
        UsageTelemetry.Current = _usage = new(_config, _time, _summaries.Add);
    }

    [TearDown]
    public void TearDown()
    {
        _usage.Dispose();
        UsageTelemetry.Current = _previous;
    }

    [TestCase(null)]
    [TestCase(false)]
    public void Missing_or_disabled_consent_records_nothing(bool? consent)
    {
        _config.Beutl_Application = consent;
        _usage.Record("tool.command", "Timeline", "Paste");
        Assert.That(_usage.Begin("export"), Is.Null);
        _usage.Flush();
        Assert.That(_summaries, Is.Empty);
    }

    [Test]
    public void Incomplete_consent_drops_queued_events()
    {
        _usage.Record("tool.command", "Timeline", "Paste");
        _config.Beutl_Logging = null;
        _usage.Flush();
        Assert.That(_summaries, Is.Empty);
    }

    [Test]
    public void Counts_batch_and_running_time_is_incremental()
    {
        for (int i = 0; i < 5; i++) _usage.Record("tool.command", "VersionControl", "Commit");
        _time.Advance(TimeSpan.FromSeconds(60));
        _usage.Flush();
        _time.Advance(TimeSpan.FromSeconds(30));
        _usage.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(_summaries.Single(s => s.Key.Event == "tool.command").Count, Is.EqualTo(5));
            Assert.That(_summaries.Count(s => s.Key.Event == "session.started"), Is.EqualTo(1));
            Assert.That(_summaries.Where(s => s.Key.Event == "session.heartbeat").Sum(s => s.DurationMs), Is.EqualTo(90_000));
            Assert.That(_summaries.Count(s => s.Key.Event == "session.ended"), Is.EqualTo(1));
        });
    }

    [Test]
    public void Revoke_and_reenable_drops_pending_and_inflight_data_and_disabled_time()
    {
        using UsageTelemetry.Operation operation = _usage.Begin("export")!;
        _usage.Record("tool.command", "Timeline", "Paste");
        _time.Advance(TimeSpan.FromSeconds(10));
        _config.Beutl_Application = false;
        _time.Advance(TimeSpan.FromHours(1));
        _config.Beutl_Application = true;
        operation.Complete();
        operation.Dispose();
        _time.Advance(TimeSpan.FromSeconds(5));
        _usage.Flush();
        Assert.Multiple(() =>
        {
            Assert.That(_summaries.Any(s => s.Key.Event is "tool.command" or "operation"), Is.False);
            Assert.That(_summaries.Single(s => s.Key.Event == "session.heartbeat").DurationMs, Is.EqualTo(5000));
            Assert.That(_summaries.Count(s => s.Key.Event == "session.started"), Is.EqualTo(1));
        });
    }

    [Test]
    public void Operation_records_failure_by_default_success_and_cancellation_once()
    {
        using (var failed = _usage.Begin("export")) _time.Advance(TimeSpan.FromMilliseconds(25));
        using (var success = _usage.Begin("export"))
        {
            _time.Advance(TimeSpan.FromMilliseconds(50));
            success!.Complete();
            success.Dispose();
        }
        using (var cancelled = _usage.Begin("export")) cancelled!.Complete("cancelled");
        _usage.Flush();
        UsageSummary[] operations = _summaries.Where(s => s.Key.Event == "operation").ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(operations, Has.Length.EqualTo(3));
            Assert.That(operations.All(s => s.Count == 1), Is.True);
            Assert.That(operations.Single(s => s.Key.Outcome == "failed").DurationMs, Is.EqualTo(25));
            Assert.That(operations.Single(s => s.Key.Outcome == "succeeded").DurationMs, Is.EqualTo(50));
        });
    }

    [Test]
    public void Emitter_failure_and_reentrant_revocation_do_not_escape_flush()
    {
        int calls = 0;
        using var usage = new UsageTelemetry(_config, _time, _ =>
        {
            calls++;
            _config.Beutl_Application = false;
            throw new InvalidOperationException("unavailable exporter");
        });
        usage.Record("tool.command", "Timeline", "Paste");
        Assert.DoesNotThrow(usage.Flush);
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void Emitted_schema_contains_counts_and_no_user_content()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == UsageTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add
        };
        ActivitySource.AddActivityListener(listener);
        using var parent = new Activity("parent").Start();
        using var usage = new UsageTelemetry(_config, _time);
        usage.Record("tool.command", "VersionControl", "CommitCommand");
        usage.Flush();
        Assert.That(Activity.Current, Is.SameAs(parent));
        Assert.That(activities.Any(a => Equals(a.GetTagItem("beutl.usage.event"), "tool.command")), Is.True,
            string.Join("; ", activities.Select(a => a.DisplayName + ": " + string.Join(",", a.TagObjects))));
        Activity action = activities.Single(a => Equals(a.GetTagItem("beutl.usage.event"), "tool.command"));
        Assert.Multiple(() =>
        {
            Assert.That(action.ParentId, Is.Null);
            Assert.That(action.GetTagItem("beutl.usage.schema_version"), Is.EqualTo(1));
            Assert.That(action.GetTagItem("beutl.usage.count"), Is.EqualTo(1L));
            Assert.That(action.TagObjects.Select(t => t.Key), Is.EquivalentTo(new[]
            { "beutl.usage.schema_version", "beutl.usage.event", "beutl.usage.tool", "beutl.usage.feature",
                "beutl.usage.outcome", "beutl.usage.count", "beutl.usage.duration_ms" }));
        });
    }

    [Test]
    public void History_counts_commits_but_not_undo_redo_or_untrusted_names()
    {
        var scene = new Scene(640, 480, "usage-test");
        var sequence = new OperationSequenceGenerator();
        using var observer = new CoreObjectOperationObserver(null, scene, sequence);
        using var history = new HistoryManager(scene, sequence);
        using var observation = history.Subscribe(observer);
        using var tracker = new EditorUsageTracker(scene, history);
        scene.Name = "secret project title";
        history.Commit("secret command", "secret expression");
        history.Undo();
        history.Redo();
        history.Commit(); // no-op
        _usage.Flush();
        UsageSummary edit = _summaries.Single(s => s.Key.Event == "editor.edit");
        Assert.That(edit.Count, Is.EqualTo(1));
        Assert.That(edit.Key.Feature, Is.EqualTo("PropertyEdit"));
        Assert.That(EditorUsageTracker.GetCommandId("CommandNames.SplitElement"), Is.EqualTo("SplitElement"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Animation_and_expression_assignments_and_removal_report_the_owning_property(bool expression)
    {
        var scene = new Scene(640, 480, "usage-test");
        var shape = new Beutl.Graphics.Shapes.RectShape();
        var element = new Element();
        element.Objects.Add(shape);
        scene.Children.Add(element);
        var sequence = new OperationSequenceGenerator();
        using var observer = new CoreObjectOperationObserver(null, scene, sequence);
        using var history = new HistoryManager(scene, sequence);
        using var subscription = history.Subscribe(observer);
        using var tracker = new EditorUsageTracker(scene, history);
        shape.Opacity.CurrentValue = 90;
        history.Commit();
        _usage.Flush();
        Assert.That(_summaries.Single(s => s.Key.Event == "editor.property").Key.Feature, Is.EqualTo("RectShape.Opacity"));
        _summaries.Clear();

        if (expression) shape.Opacity.Expression = new StringExpression<float>("42 + 8");
        else shape.Opacity.Animation = new KeyFrameAnimation<float>();
        history.Commit();
        _usage.Flush();
        UsageSummary assignment = _summaries.Single(s => s.Key.Event == "editor.property");
        Assert.That(assignment.Key.Feature, Is.EqualTo("RectShape.Opacity"));
        Assert.That(assignment.Count, Is.EqualTo(1));
        _summaries.Clear();

        if (expression) shape.Opacity.Expression = null;
        else shape.Opacity.Animation = null;
        history.Commit();
        _usage.Flush();
        UsageSummary removal = _summaries.Single(s => s.Key.Event == "editor.property");
        Assert.That(removal.Key.Feature, Is.EqualTo("RectShape.Opacity"));
        Assert.That(removal.Count, Is.EqualTo(1));
    }

    [Test]
    public void Direct_animation_and_expression_changes_count_one_property_per_transaction()
    {
        var scene = new Scene(640, 480, "usage-test");
        var shape = new Beutl.Graphics.Shapes.RectShape();
        var element = new Element();
        element.Objects.Add(shape);
        scene.Children.Add(element);
        var sequence = new OperationSequenceGenerator();
        using var observer = new CoreObjectOperationObserver(null, scene, sequence);
        using var history = new HistoryManager(scene, sequence);
        using var subscription = history.Subscribe(observer);
        using var tracker = new EditorUsageTracker(scene, history);
        shape.Opacity.CurrentValue = 90;
        shape.Opacity.Animation = new KeyFrameAnimation<float>();
        shape.Opacity.Expression = new StringExpression<float>("42");
        history.Commit();
        _usage.Flush();
        UsageSummary property = _summaries.Single(s => s.Key.Event == "editor.property");
        Assert.That(property.Key.Feature, Is.EqualTo("RectShape.Opacity"));
        Assert.That(property.Count, Is.EqualTo(1));
        Assert.That(_summaries.Single(s => s.Key.Event == "editor.edit").Count, Is.EqualTo(1));
    }

    [Test]
    public void Effects_are_counted_once_per_type_and_disabled_effects_are_excluded()
    {
        var scene = new Scene(640, 480, "usage-test");
        var shape = new Beutl.Graphics.Shapes.RectShape();
        var group = new FilterEffectGroup();
        group.Children.Add(new Blur());
        group.Children.Add(new Blur());
        group.Children.Add(new DropShadow { IsEnabled = false });
        shape.FilterEffect.CurrentValue = group;
        var element = new Element();
        element.Objects.Add(shape);
        scene.Children.Add(element);
        var sequence = new OperationSequenceGenerator();
        using var observer = new CoreObjectOperationObserver(null, scene, sequence);
        using var history = new HistoryManager(scene, sequence);
        using var observation = history.Subscribe(observer);
        using var tracker = new EditorUsageTracker(scene, history);
        scene.Name = "changed";
        history.Commit();
        _usage.Flush();
        Assert.That(_summaries.Where(s => s.Key.Event == "effect.used").Select(s => (s.Key.Feature, s.Count)),
            Is.EquivalentTo(new[] { ("Blur", 1L) }));
    }

    [AvaloniaTest]
    public void Button_tracking_ignores_content_parameters_and_removes_replaced_handlers()
    {
        var button = new Button { Content = "private title", CommandParameter = "private path" };
        UsageTracking.SetFeature(button, "WebBrowser.Reload");
        UsageTracking.SetFeature(button, "WebBrowser.Back");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        UsageTracking.SetFeature(button, null);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        _usage.Flush();
        UsageSummary action = _summaries.Single(s => s.Key.Event == "tool.action");
        Assert.That(action.Key, Is.EqualTo(new UsageKey("tool.action", "WebBrowser", "Back")));
        Assert.That(action.Count, Is.EqualTo(1));
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void Submenu_actions_are_recorded_once_at_the_nearest_annotation(bool annotateChild, bool generatedChild)
    {
        var parent = new MenuItem { Header = "Zoom" };
        MenuItem? child = generatedChild ? null : new MenuItem { Header = "200%", CommandParameter = "private parameter" };
        if (generatedChild) parent.ItemsSource = new[] { "200%" };
        else parent.Items.Add(child);
        UsageTracking.SetFeature(parent, "GraphEditor.Zoom");
        int invoked = 0;
        parent.AddHandler(MenuItem.ClickEvent, (_, args) =>
        {
            if (ReferenceEquals(args.Source, child)) invoked++;
        });
        var menu = new Menu();
        menu.Items.Add(parent);
        var window = new Window { Content = menu };
        window.Show();
        try
        {
            parent.IsSubMenuOpen = true;
            HeadlessTestHelpers.Settle();
            child ??= (MenuItem)parent.ContainerFromIndex(0)!;
            Assert.That(child, Is.Not.Null);
            if (annotateChild) UsageTracking.SetFeature(child, "GraphEditor.FitAll");
            for (int i = 0; i < 2; i++) child.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            _usage.Flush();
            Assert.That(invoked, Is.EqualTo(2));
            UsageSummary action = _summaries.Single(s => s.Key.Event == "tool.action");
            Assert.That(action.Key.Feature, Is.EqualTo(annotateChild ? "FitAll" : "Zoom"));
            Assert.That(action.Count, Is.EqualTo(2));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void Graph_actions_keep_distinct_feature_names()
    {
        var view = new GraphEditorView();
        Panel panel = view.FindControl<Panel>("graphPanel")!;
        MenuItem[] items = panel.ContextMenu!.Items.OfType<MenuItem>()
            .Where(item => item.Tag is "ValueGraph" or "SpeedGraph" or "Ease" or "EaseIn" or "EaseOut").ToArray();
        Assert.That(items, Has.Length.EqualTo(5));
        foreach (MenuItem item in items) item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        _usage.Flush();
        UsageSummary[] actions = _summaries.Where(s => s.Key.Event == "tool.action").ToArray();
        Assert.That(actions.Select(action => action.Key.Feature),
            Is.EquivalentTo(new[] { "ValueGraph", "SpeedGraph", "Ease", "EaseIn", "EaseOut" }));
        Assert.That(actions.All(action => action.Count == 1), Is.True);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Path_actions_distinguish_segment_types_and_drag_modes(bool toolTab)
    {
        Control view = toolTab ? new PathEditorTabView() : new PathEditorView();
        Canvas canvas = view.FindControl<Canvas>("canvas")!;
        var menu = (FAMenuFlyout)canvas.ContextFlyout!;
        FAMenuFlyoutItem[] items = menu.Items.OfType<FAMenuFlyoutItem>()
            .Where(item => item.Tag is "Cubic" or "Quad" or "Line" or "Arc" or "Conic"
                or "Symmetry" or "Asymmetry" or "Separately").ToArray();
        Assert.That(items, Has.Length.EqualTo(8));
        foreach (var item in items) item.RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
        _usage.Flush();
        Assert.That(_summaries.Where(s => s.Key.Event == "tool.action").Select(s => (s.Key.Feature, s.Count)),
            Is.EquivalentTo(new[] { ("AddCubicSegment", 1L), ("AddQuadSegment", 1L), ("AddLineSegment", 1L),
                ("AddArcSegment", 1L), ("AddConicSegment", 1L), ("DragMode.Symmetry", 1L),
                ("DragMode.Asymmetry", 1L), ("DragMode.Separately", 1L) }));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Opting_in_records_existing_effects_without_an_edit_and_does_not_double_count(bool fromBackground)
    {
        _config.Beutl_Application = false;
        var scene = new Scene(640, 480, "usage-test");
        var shape = new Beutl.Graphics.Shapes.RectShape();
        shape.FilterEffect.CurrentValue = new Blur();
        var element = new Element();
        element.Objects.Add(shape);
        scene.Children.Add(element);
        using var history = new HistoryManager(scene, new OperationSequenceGenerator());
        using var tracker = new EditorUsageTracker(scene, history);
        _usage.Flush();
        Assert.That(_summaries, Is.Empty);

        if (fromBackground) Task.Run(() => _config.Beutl_Application = true).GetAwaiter().GetResult();
        else _config.Beutl_Application = true;
        HeadlessTestHelpers.Settle();
        _usage.Flush();
        Assert.That(_summaries.Single(s => s.Key.Event == "effect.used").Key.Feature, Is.EqualTo("Blur"));
        Assert.That(history.UndoCount, Is.Zero);

        _config.Beutl_Application = false;
        _config.Beutl_Application = true;
        _usage.Flush();
        Assert.That(_summaries.Count(s => s.Key.Event == "effect.used"), Is.EqualTo(1));
    }

    [AvaloniaTest]
    public void Closing_an_editor_before_queued_opt_in_does_not_scan_it()
    {
        _config.Beutl_Application = false;
        var scene = new Scene(640, 480, "usage-test");
        var shape = new Beutl.Graphics.Shapes.RectShape();
        shape.FilterEffect.CurrentValue = new Blur();
        var element = new Element();
        element.Objects.Add(shape);
        scene.Children.Add(element);
        using var history = new HistoryManager(scene, new OperationSequenceGenerator());
        var tracker = new EditorUsageTracker(scene, history);
        Task.Run(() => _config.Beutl_Application = true).GetAwaiter().GetResult();
        tracker.Dispose();
        HeadlessTestHelpers.Settle();
        _config.Beutl_Application = false;
        _config.Beutl_Application = true;
        _usage.Flush();
        Assert.That(_summaries.Any(s => s.Key.Event == "effect.used"), Is.False);
    }

    [Test]
    public void Failing_opt_in_observer_does_not_block_other_observers_or_configuration()
    {
        _config.Beutl_Application = false;
        _config.Beutl_Logging = null;
        int notifications = 0;
        _usage.CollectionEnabled += () => throw new InvalidOperationException("failed observer");
        _usage.CollectionEnabled += () => notifications++;
        _config.Beutl_Application = true;
        Assert.That(notifications, Is.Zero, "Incomplete consent must not trigger collection.");
        Assert.DoesNotThrow(() => _config.Beutl_Logging = false);
        Assert.That(notifications, Is.EqualTo(1));
        _config.Beutl_Logging = true;
        Assert.That(notifications, Is.EqualTo(1), "Unchanged application consent must not rescan.");
    }

    [Test]
    public void Tab_open_and_real_interaction_are_distinct()
    {
        var scene = new Scene(640, 480, "usage-test");
        using var history = new HistoryManager(scene, new OperationSequenceGenerator());
        using var tracker = new EditorUsageTracker(scene, history);
        object first = new(), second = new();
        tracker.ActivateTool(first, "ColorScopes");
        tracker.ActivateTool(first, "ColorScopes");
        tracker.ActivateTool(second, "NodeGraph");
        tracker.ActivateTool(first, "ColorScopes");
        _usage.Flush();
        Assert.That(_summaries.Single(s => s.Key.Event == "tool.activated" && s.Key.Tool == "ColorScopes").Count, Is.EqualTo(2));
        Assert.That(ToolUsageTracker.GetToolId(typeof(UsageTelemetryTests)), Is.EqualTo("Extension"));
    }

    [Test]
    public void Tab_interaction_captured_before_revoke_is_not_recorded_after_reenable()
    {
        Assert.That(_usage.TryGetCollectionEpoch(out long previous), Is.True);
        _config.Beutl_Application = false;
        _config.Beutl_Application = true;
        _usage.Record("tool.activated", "Timeline", epoch: previous);
        _usage.Flush();
        Assert.That(_summaries.Any(s => s.Key.Event == "tool.activated"), Is.False);

        Assert.That(_usage.TryGetCollectionEpoch(out long current), Is.True);
        Assert.That(current, Is.Not.EqualTo(previous));
        _usage.Record("tool.activated", "Timeline", epoch: current);
        _usage.Flush();
        Assert.That(_summaries.Single(s => s.Key.Event == "tool.activated").Count, Is.EqualTo(1));
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void First_tab_interaction_in_each_enabled_period_is_recorded_once(bool previouslyEnabled, bool fromBackground)
    {
        _config.Beutl_Application = previouslyEnabled;
        var scene = new Scene(640, 480, "usage-test");
        using var history = new HistoryManager(scene, new OperationSequenceGenerator());
        using var tracker = new EditorUsageTracker(scene, history);
        object timeline = new();
        tracker.ActivateTool(timeline, "Timeline");
        _usage.Flush();
        _summaries.Clear();

        _config.Beutl_Application = false;
        tracker.ActivateTool(timeline, "Timeline");
        Assert.That(tracker.ActiveTool, Is.EqualTo("Timeline"));
        _usage.Flush();
        Assert.That(_summaries, Is.Empty);
        if (fromBackground) Task.Run(() => _config.Beutl_Application = true).GetAwaiter().GetResult();
        else _config.Beutl_Application = true;
        _usage.Flush();
        Assert.That(_summaries.Any(s => s.Key.Event == "tool.activated"), Is.False,
            "Enabling collection alone is not a tab interaction.");

        // In the background case this happens before the posted UI scan runs.
        tracker.ActivateTool(timeline, "Timeline");
        tracker.ActivateTool(timeline, "Timeline");
        _usage.Flush();
        Assert.That(_summaries.Single(s => s.Key.Event == "tool.activated").Count, Is.EqualTo(1));
        HeadlessTestHelpers.Settle();
        _config.Beutl_Logging = true;
        tracker.ActivateTool(timeline, "Timeline");
        _usage.Flush();
        Assert.That(_summaries.Where(s => s.Key.Event == "tool.activated").Sum(s => s.Count), Is.EqualTo(1),
            "The deferred scan and unrelated settings must not reset active-period deduplication.");
    }

    [AvaloniaTest]
    public void Scope_mode_changes_require_interaction_and_never_capture_image_content()
    {
        var player = new Mock<IPreviewPlayer>();
        player.SetupGet(p => p.PreviewImage).Returns(new ReactivePropertySlim<Ref<Bitmap>?>());
        player.SetupGet(p => p.AfterRendered).Returns(Observable.Never<System.Reactive.Unit>());
        var editor = new Mock<IEditorContext>();
        editor.Setup(e => e.GetService(typeof(IPreviewPlayer))).Returns(player.Object);
        using var context = new ColorScopesTabViewModel(editor.Object);
        using var tracker = new ToolUsageTracker(context);
        context.IsSelected.Value = true;
        context.SelectedScopeType.Value = ColorScopeType.Histogram; // restored state
        _usage.Flush();
        Assert.That(_summaries.Any(s => s.Key.Event == "tool.setting"), Is.False);
        tracker.Interact();
        context.SelectedScopeType.Value = ColorScopeType.Vectorscope;
        _usage.Flush();
        Assert.That(_summaries.Single(s => s.Key.Event == "tool.setting").Key,
            Is.EqualTo(new UsageKey("tool.setting", "ColorScopes", "SelectedScopeType.Vectorscope")));
    }

    [AvaloniaTest]
    public async Task Real_tool_commands_are_observed_across_reactive_command_variants()
    {
        await TestReset.ResetShellAsync();
        try
        {
            string directory = Path.Combine(BeutlHomeIsolation.CurrentHome!, "usage-commands");
            Directory.CreateDirectory(directory);
            Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "commands", directory))!;
            Scene scene = project.Items.OfType<Scene>().Single();
            TestShell.Editor.ActivateTabItem(scene);
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
            timeline.Duplicate.Execute();
            timeline.SetStartTimeToCurrentTime.Execute();
            await timeline.Paste.ExecuteAsync();
            _usage.Flush();
            foreach (string feature in new[] { "Duplicate", "SetStartTimeToCurrentTime", "Paste" })
                Assert.That(_summaries.Single(s => s.Key.Event == "tool.command" && s.Key.Feature == feature).Count,
                    Is.EqualTo(1), feature);
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        internal void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new NoopTimer();
        private sealed class NoopTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
