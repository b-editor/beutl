using System.Numerics;
using System.Reactive.Linq;

using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.ProjectSystem;

using Reactive.Bindings;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class TimelineTabViewModelTests
{
    [Test]
    public void DirectRazorModeSet_ClearsActiveTrimMode()
    {
        using TimelineTabViewModel viewModel = CreateViewModel();
        viewModel.IsSlipMode.Value = true;

        viewModel.IsRazorMode.Value = true;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsRazorMode.Value, Is.True);
            Assert.That(viewModel.IsSlipMode.Value, Is.False);
            Assert.That(viewModel.IsRollMode.Value, Is.False);
            Assert.That(viewModel.IsSlideMode.Value, Is.False);
        });
    }

    [Test]
    public void DirectTrimModeSet_ClearsRazorAndOtherTrimModes()
    {
        using TimelineTabViewModel viewModel = CreateViewModel();
        viewModel.IsRazorMode.Value = true;

        viewModel.IsRollMode.Value = true;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsRazorMode.Value, Is.False);
            Assert.That(viewModel.IsSlipMode.Value, Is.False);
            Assert.That(viewModel.IsRollMode.Value, Is.True);
            Assert.That(viewModel.IsSlideMode.Value, Is.False);
        });

        viewModel.IsSlideMode.Value = true;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsRazorMode.Value, Is.False);
            Assert.That(viewModel.IsSlipMode.Value, Is.False);
            Assert.That(viewModel.IsRollMode.Value, Is.False);
            Assert.That(viewModel.IsSlideMode.Value, Is.True);
        });
    }

    // The V/Escape gestures are shared by every Exit* command; the dispatcher relies on
    // CanExecute being true only for the active mode to fall through to the right one.
    [Test]
    public void CanExecute_ExitCommands_TrueOnlyForActiveMode()
    {
        using TimelineTabViewModel viewModel = CreateViewModel();
        viewModel.IsRollMode.Value = true;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanExecute(new ContextCommandExecution("ExitRollMode")), Is.True);
            Assert.That(viewModel.CanExecute(new ContextCommandExecution("ExitRazorMode")), Is.False);
            Assert.That(viewModel.CanExecute(new ContextCommandExecution("ExitSlipMode")), Is.False);
            Assert.That(viewModel.CanExecute(new ContextCommandExecution("ExitSlideMode")), Is.False);
        });
    }

    [Test]
    public void CanExecute_ExitCommands_AllFalseWhenNoModeActive()
    {
        using TimelineTabViewModel viewModel = CreateViewModel();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanExecute(new ContextCommandExecution("ExitRazorMode")), Is.False);
            Assert.That(viewModel.CanExecute(new ContextCommandExecution("ExitSlipMode")), Is.False);
            Assert.That(viewModel.CanExecute(new ContextCommandExecution("ExitRollMode")), Is.False);
            Assert.That(viewModel.CanExecute(new ContextCommandExecution("ExitSlideMode")), Is.False);
        });
    }

    // A live tracked-layer-top subscription is what closes the loop — without a subscriber the
    // synchronous Height emits reach nothing and no re-entry happens at all.
    [Test]
    public void CalculateLayerTop_DeepLayer_WithTrackedSubscriber_GrowsWithoutRecursing()
    {
        using TimelineTabViewModel viewModel = CreateViewModel();
        using IDisposable subscription = viewModel.GetTrackedLayerTopObservable(1).Subscribe(_ => { });

        double top = viewModel.CalculateLayerTop(2000);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.LayerHeaders, Has.Count.GreaterThanOrEqualTo(2001));
            Assert.That(
                top,
                Is.EqualTo(viewModel.LayerHeaders.Take(2000).Sum(h => h.Height.Value)));
        });
    }

    // The subscriber's own layer is above the growth, so every new header's height feeds it and
    // it must still end holding the completed sum, not a partial one.
    [Test]
    public void TrackedLayerTop_DeepSubscriber_EndsWithCompletedSum()
    {
        using TimelineTabViewModel viewModel = CreateViewModel();
        double observed = double.NaN;
        using IDisposable subscription = viewModel.GetTrackedLayerTopObservable(2000)
            .Subscribe(v => observed = v);

        viewModel.CalculateLayerTop(2000);

        Assert.That(observed, Is.EqualTo(viewModel.LayerHeaders.Take(2000).Sum(h => h.Height.Value)));
    }

    [Test]
    public void ToLayerNumber_FarBelowLastHeader_WithTrackedSubscriber_GrowsWithoutRecursing()
    {
        using TimelineTabViewModel viewModel = CreateViewModel();
        using IDisposable subscription = viewModel.GetTrackedLayerTopObservable(1).Subscribe(_ => { });
        double deepOffset = viewModel.CalculateLayerTop(1) * 2000;

        int layer = viewModel.ToLayerNumber(deepOffset);

        Assert.Multiple(() =>
        {
            Assert.That(layer, Is.GreaterThanOrEqualTo(2000));
            Assert.That(viewModel.LayerHeaders, Has.Count.GreaterThan(layer));
        });
    }

    private static TimelineTabViewModel CreateViewModel()
    {
        var scene = new Scene();
        var editorContext = new TestEditorContext(scene);
        editorContext.AddService<ITimelineOptionsProvider>(new TestTimelineOptionsProvider(scene));
        editorContext.AddService<IEditorClock>(new TestEditorClock());
        editorContext.AddService<IBufferStatus>(new TestBufferStatus());
        return new TimelineTabViewModel(editorContext);
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

    private sealed class TestTimelineOptionsProvider(Scene scene) : ITimelineOptionsProvider
    {
        public Scene Scene { get; } = scene;

        public IReactiveProperty<TimelineOptions> Options { get; } =
            new ReactiveProperty<TimelineOptions>(new TimelineOptions(1, Vector2.Zero, 0));

        public IObservable<float> Scale => Options.Select(x => x.Scale);

        public IObservable<Vector2> Offset => Options.Select(x => x.Offset);
    }

    private sealed class TestEditorClock : IEditorClock
    {
        public IReactiveProperty<TimeSpan> CurrentTime { get; } = new ReactivePropertySlim<TimeSpan>(TimeSpan.Zero);

        public IReadOnlyReactiveProperty<TimeSpan> MaximumTime { get; } =
            new ReactivePropertySlim<TimeSpan>(TimeSpan.FromSeconds(30));
    }

    private sealed class TestBufferStatus : IBufferStatus
    {
        public IReadOnlyReactiveProperty<TimeSpan> StartTime { get; } =
            new ReactivePropertySlim<TimeSpan>(TimeSpan.Zero);

        public IReadOnlyReactiveProperty<TimeSpan> EndTime { get; } =
            new ReactivePropertySlim<TimeSpan>(TimeSpan.Zero);

        public IReadOnlyReactiveProperty<double> Start { get; } = new ReactivePropertySlim<double>(0);

        public IReadOnlyReactiveProperty<double> End { get; } = new ReactivePropertySlim<double>(0);

        public IReadOnlyReactiveProperty<CacheBlock[]> CacheBlocks { get; } =
            new ReactivePropertySlim<CacheBlock[]>([]);

        public void UpdateBlocks()
        {
        }

        public void ClearCache()
        {
        }

        public void LockCache(int startFrame, int endFrame)
        {
        }

        public void UnlockCache(int startFrame, int endFrame)
        {
        }

        public void DeleteCache(int startFrame, int endFrame)
        {
        }

        public long CalculateCacheByteCount(int startFrame, int endFrame)
        {
            return 0;
        }
    }
}
