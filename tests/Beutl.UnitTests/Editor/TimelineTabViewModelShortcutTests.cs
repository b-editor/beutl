using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.UnitTests.TestInfrastructure;
using Reactive.Bindings;

namespace Beutl.UnitTests.Editor;

[TestFixture]
[NonParallelizable]
public class TimelineTabViewModelShortcutTests
{
    private static readonly CaptureNotificationHandler s_notificationHandler = new();
    private INotificationServiceHandler? _previousHandler;

    [OneTimeSetUp]
    public void InstallNotificationHandler()
    {
        _previousHandler = NotificationService.Handler;
        NotificationService.Handler = s_notificationHandler;
    }

    [OneTimeTearDown]
    public void RestoreNotificationHandler()
    {
        // The setter rejects null; when no handler existed before, ours stays (Show is null-safe
        // and no other fixture asserts on Handler identity).
        if (_previousHandler is not null)
        {
            NotificationService.Handler = _previousHandler;
        }
    }

    [SetUp]
    public void ClearCapturedNotifications()
    {
        s_notificationHandler.Notifications.Clear();
    }

    [Test]
    public void IsTextInputSource_ReturnsTrue_ForTextBox()
    {
        Assert.That(TimelineTabViewModel.IsTextInputSource(new TextBox()), Is.True);
    }

    [Test]
    public void IsTextInputSource_ReturnsFalse_ForNonTextVisual()
    {
        Assert.That(TimelineTabViewModel.IsTextInputSource(new Border()), Is.False);
    }

    [Test]
    public void IsTextInputSource_ReturnsFalse_ForNull()
    {
        Assert.That(TimelineTabViewModel.IsTextInputSource(null), Is.False);
    }

    [Test]
    public void CloseGapCommand_FlushesPendingNudgeBeforeGapService()
    {
        using var harness = new SceneHistoryHarness("beutl_timeline_gap_command", duration: TimeSpan.FromSeconds(30));
        Element anchor = harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        var nudgeService = new CaptureNudgeService();
        var gapService = new CaptureGapService(nudgeService);
        TimelineTabViewModel viewModel = CreateViewModel(harness.Scene, nudgeService, gapService, anchor);

        InvokePrivate(viewModel, "CloseSelectedGap");

        Assert.Multiple(() =>
        {
            Assert.That(nudgeService.FlushCount, Is.EqualTo(1));
            Assert.That(gapService.CloseGapCalled, Is.True);
            Assert.That(gapService.CloseGapSawFlush, Is.True);
        });
    }

    [Test]
    public void CloseGapCommand_NoSelection_NotifiesWithoutCallingGapService()
    {
        using var harness = new SceneHistoryHarness("beutl_timeline_gap_command", duration: TimeSpan.FromSeconds(30));
        var nudgeService = new CaptureNudgeService();
        var gapService = new CaptureGapService(nudgeService);
        TimelineTabViewModel viewModel = CreateViewModel(harness.Scene, nudgeService, gapService);

        InvokePrivate(viewModel, "CloseSelectedGap");

        Assert.Multiple(() =>
        {
            Assert.That(gapService.CloseGapCalled, Is.False);
            Assert.That(nudgeService.FlushCount, Is.Zero);
            Assert.That(s_notificationHandler.Notifications, Has.Count.EqualTo(1));
            Assert.That(s_notificationHandler.Notifications[0].Title, Is.EqualTo(Strings.CloseGap));
            Assert.That(s_notificationHandler.Notifications[0].Message, Is.EqualTo(Strings.NoElementSelected));
        });
    }

    [Test]
    public void FindGapNavigationTarget_Forward_ReturnsGapCenter()
    {
        using var harness = new SceneHistoryHarness("beutl_timeline_gap_nav", duration: TimeSpan.FromSeconds(30));
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));

        TimeSpan? target = TimelineTabViewModel.FindGapNavigationTarget(
            harness.Scene, TimeSpan.Zero, forward: true)?.Target;

        Assert.That(target, Is.EqualTo(TimeSpan.FromSeconds(4)));
    }

    [Test]
    public void FindGapNavigationTarget_Forward_NoGapAhead_ReturnsNull()
    {
        using var harness = new SceneHistoryHarness("beutl_timeline_gap_nav", duration: TimeSpan.FromSeconds(30));
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));

        TimeSpan? target = TimelineTabViewModel.FindGapNavigationTarget(
            harness.Scene, TimeSpan.FromSeconds(10), forward: true)?.Target;

        Assert.That(target, Is.Null);
    }

    [Test]
    public void FindGapNavigationTarget_Forward_CurrentBeforeSceneStart_FindsFirstInRangeGap()
    {
        using var harness = new SceneHistoryHarness(
            "beutl_timeline_gap_nav",
            start: TimeSpan.FromSeconds(10),
            duration: TimeSpan.FromSeconds(20));
        harness.AddElement(TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(2));
        harness.AddElement(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(2));

        TimeSpan? target = TimelineTabViewModel.FindGapNavigationTarget(
            harness.Scene, TimeSpan.Zero, forward: true)?.Target;

        Assert.That(target, Is.EqualTo(TimeSpan.FromSeconds(14)));
    }

    [Test]
    public void FindGapNavigationTarget_Forward_CurrentBeforeSceneStart_ReturnsBoundaryTouchingGap()
    {
        using var harness = new SceneHistoryHarness(
            "beutl_timeline_gap_nav",
            start: TimeSpan.FromSeconds(10),
            duration: TimeSpan.FromSeconds(20));
        // Ends exactly at the scene start, so the gap [10s, 14s] begins on the boundary. It lies
        // wholly in range, so navigating forward from before the scene must land on it.
        harness.AddElement(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(1));
        harness.AddElement(TimeSpan.FromSeconds(14), TimeSpan.FromSeconds(2));

        TimeSpan? target = TimelineTabViewModel.FindGapNavigationTarget(
            harness.Scene, TimeSpan.Zero, forward: true)?.Target;

        Assert.That(target, Is.EqualTo(TimeSpan.FromSeconds(12)));
    }

    [Test]
    public void FindGapNavigationTarget_Backward_CurrentBeyondSceneEnd_ReturnsBoundaryTouchingGap()
    {
        using var harness = new SceneHistoryHarness("beutl_timeline_gap_nav", duration: TimeSpan.FromSeconds(20));
        harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        harness.AddElement(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
        // Starts exactly at the scene end, so the gap [15s, 20s] ends on the boundary. It lies wholly
        // in range, so navigating back from beyond the scene must land on it, not skip to [5s, 10s].
        harness.AddElement(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(5));

        TimeSpan? target = TimelineTabViewModel.FindGapNavigationTarget(
            harness.Scene, TimeSpan.FromSeconds(100), forward: false)?.Target;

        Assert.That(target, Is.EqualTo(TimeSpan.FromSeconds(17.5)));
    }

    [Test]
    public void FindGapNavigationTarget_NoElements_ReturnsNull()
    {
        using var harness = new SceneHistoryHarness("beutl_timeline_gap_nav", duration: TimeSpan.FromSeconds(30));

        Assert.Multiple(() =>
        {
            Assert.That(
                TimelineTabViewModel.FindGapNavigationTarget(harness.Scene, TimeSpan.Zero, forward: true),
                Is.Null);
            Assert.That(
                TimelineTabViewModel.FindGapNavigationTarget(harness.Scene, TimeSpan.Zero, forward: false),
                Is.Null);
        });
    }

    [Test]
    public void FindGapNavigationTarget_ReturnsGapAnchorForSelection()
    {
        using var harness = new SceneHistoryHarness("beutl_timeline_gap_nav", duration: TimeSpan.FromSeconds(30));
        Element a = harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));

        var result = TimelineTabViewModel.FindGapNavigationTarget(harness.Scene, TimeSpan.Zero, forward: true);

        Assert.Multiple(() =>
        {
            Assert.That(result?.Target, Is.EqualTo(TimeSpan.FromSeconds(4)));
            // The anchor is the element ending at the gap start, so GoToGap selects it and a follow-up
            // Close Gap closes this gap.
            Assert.That(result?.Anchor, Is.SameAs(a));
        });
    }

    [Test]
    public void FindGapNavigationTarget_ClippedGapStart_DoesNotSelectStaleAnchor()
    {
        using var harness = new SceneHistoryHarness(
            "beutl_timeline_gap_nav",
            start: TimeSpan.FromSeconds(50),
            duration: TimeSpan.FromSeconds(30));
        // The gap 10s-100s straddles the active range 50s-80s; its start is clipped to 50s. The anchor
        // ends off-scene at 10s, so navigation must not select it (Close Gap would collapse 10s-100s).
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        harness.AddElement(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(5));

        var result = TimelineTabViewModel.FindGapNavigationTarget(
            harness.Scene, TimeSpan.FromSeconds(40), forward: true);

        Assert.Multiple(() =>
        {
            Assert.That(result?.Target, Is.EqualTo(TimeSpan.FromSeconds(65)));
            Assert.That(result?.Anchor, Is.Null);
        });
    }

    [Test]
    public void CloseAllGapsCommand_FlushesPendingNudgeBeforeGapService()
    {
        using var harness = new SceneHistoryHarness("beutl_timeline_gap_command", duration: TimeSpan.FromSeconds(30));
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        var nudgeService = new CaptureNudgeService();
        var gapService = new CaptureGapService(nudgeService);
        TimelineTabViewModel viewModel = CreateViewModel(harness.Scene, nudgeService, gapService);

        InvokePrivate(viewModel, "CloseAllSceneGaps");

        Assert.Multiple(() =>
        {
            Assert.That(nudgeService.FlushCount, Is.EqualTo(1));
            Assert.That(gapService.CloseAllGapsCalled, Is.True);
            Assert.That(gapService.CloseAllGapsSawFlush, Is.True);
        });
    }

    private static TimelineTabViewModel CreateViewModel(
        Scene scene,
        IElementNudgeService nudgeService,
        IElementGapService gapService,
        Element? selectedElement = null)
    {
        var context = new TestEditorContext(scene);
        context.AddService<IElementNudgeService>(nudgeService);
        context.AddService<IElementGapService>(gapService);

        var viewModel = (TimelineTabViewModel)RuntimeHelpers.GetUninitializedObject(typeof(TimelineTabViewModel));
        // GetUninitializedObject skips field initializers, so any finalizer would run against null
        // fields and crash the finalizer thread (killing the test host). Suppress it.
        GC.SuppressFinalize(viewModel);
        SetAutoProperty(viewModel, nameof(TimelineTabViewModel.Scene), scene);
        SetAutoProperty(viewModel, nameof(TimelineTabViewModel.EditorContext), context);
        SetAutoProperty(viewModel, nameof(TimelineTabViewModel.SelectedElements), CreateSelection(selectedElement));
        return viewModel;
    }

    private static HashSet<ElementViewModel> CreateSelection(Element? element)
    {
        var selection = new HashSet<ElementViewModel>();
        if (element is not null)
        {
            selection.Add(CreateElementViewModel(element));
        }

        return selection;
    }

    private static ElementViewModel CreateElementViewModel(Element element)
    {
        var viewModel = (ElementViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ElementViewModel));
        // ElementViewModel's finalizer dereferences a field that GetUninitializedObject leaves null.
        GC.SuppressFinalize(viewModel);
        SetAutoProperty(viewModel, nameof(ElementViewModel.Model), element);
        return viewModel;
    }

    private static void InvokePrivate(TimelineTabViewModel viewModel, string methodName)
    {
        MethodInfo method = typeof(TimelineTabViewModel).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(TimelineTabViewModel).FullName, methodName);
        method.Invoke(viewModel, null);
    }

    private static void SetAutoProperty<TTarget, TValue>(TTarget target, string propertyName, TValue value)
        where TTarget : notnull
    {
        FieldInfo field = typeof(TTarget).GetField(
            $"<{propertyName}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(TTarget).FullName, propertyName);
        field.SetValue(target, value);
    }

    private sealed class CaptureNotificationHandler : INotificationServiceHandler
    {
        public List<Notification> Notifications { get; } = [];

        public void Show(Notification notification) => Notifications.Add(notification);
    }

    private sealed class CaptureNudgeService : IElementNudgeService
    {
        public int FlushCount { get; private set; }

        public void Nudge(Scene scene, IReadOnlyList<Element> targets, int frames)
        {
        }

        public void Flush()
        {
            FlushCount++;
        }

        public void Dispose()
        {
        }
    }

    private sealed class CaptureGapService(CaptureNudgeService nudgeService) : IElementGapService
    {
        public bool CloseGapCalled { get; private set; }
        public bool CloseGapSawFlush { get; private set; }
        public bool CloseAllGapsCalled { get; private set; }
        public bool CloseAllGapsSawFlush { get; private set; }

        public bool CloseGapAfter(Scene scene, Element anchor)
        {
            CloseGapCalled = true;
            CloseGapSawFlush = nudgeService.FlushCount > 0;
            return true;
        }

        public int CloseAllGaps(Scene scene)
        {
            CloseAllGapsCalled = true;
            CloseAllGapsSawFlush = nudgeService.FlushCount > 0;
            return 1;
        }
    }

    private sealed class TestEditorContext(CoreObject obj) : IEditorContext
    {
        private readonly Dictionary<Type, object> _services = [];

        public CoreObject Object { get; } = obj;

        public EditorExtension Extension => null!;

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

        public IKnownEditorCommands? Commands => null;

        public void AddService<T>(T service)
            where T : notnull
        {
            _services[typeof(T)] = service;
        }

        public object? GetService(Type serviceType)
        {
            return _services.GetValueOrDefault(serviceType);
        }

        public T? FindToolTab<T>(Func<T, bool> condition)
            where T : IToolContext
        {
            return default;
        }

        public T? FindToolTab<T>()
            where T : IToolContext
        {
            return default;
        }

        public bool OpenToolTab(IToolContext item)
        {
            return false;
        }

        public void CloseToolTab(IToolContext item)
        {
        }
    }
}
