using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;
using Reactive.Bindings;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class TimelineTabViewModelShortcutTests
{
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

        public bool CloseGap(Scene scene, Element anchor)
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
