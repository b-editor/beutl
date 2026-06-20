using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.TestInfrastructure;

/// <summary>
/// Wires the repeated <see cref="OperationSequenceGenerator"/> + <see cref="HistoryManager"/> +
/// <see cref="CoreObjectOperationObserver"/> + <c>Subscribe</c> setup that the editor-service fixtures
/// share, and disposes the observer and history in reverse order on teardown.
/// </summary>
/// <remarks>
/// This is the non-Scene-specific portion: any fixture whose root is a plain <see cref="CoreObject"/>
/// (an <see cref="Element"/>, a node graph, a bespoke test model, ...) can reuse it directly.
/// Scene-rooted fixtures that also need a backing temp directory use <see cref="SceneHistoryHarness"/>,
/// which composes this type.
/// </remarks>
public sealed class HistoryHarness : IDisposable
{
    public HistoryHarness(CoreObject root)
    {
        Root = root;
        Sequence = new OperationSequenceGenerator();
        History = new HistoryManager(root, Sequence);
        Observer = new CoreObjectOperationObserver(null, root, Sequence);
        History.Subscribe(Observer);
    }

    public CoreObject Root { get; }

    public OperationSequenceGenerator Sequence { get; }

    public HistoryManager History { get; }

    public CoreObjectOperationObserver Observer { get; }

    public void Dispose()
    {
        Observer.Dispose();
        History.Dispose();
    }
}

/// <summary>
/// Disposable harness for the Scene-rooted editor-service fixtures that persist elements to disk. It
/// owns a temp working directory, builds a <see cref="Scene"/> whose <see cref="Scene.Uri"/> points
/// into it, and exposes a <see cref="HistoryHarness"/> so the history/observer plumbing is shared with
/// the non-Scene fixtures. Each fixture keeps its own dimensions, time range, and temp-directory prefix
/// by passing them to the constructor.
/// </summary>
public sealed class SceneHistoryHarness : IDisposable
{
    private readonly HistoryHarness _history;

    /// <param name="namePrefix">
    /// Prefix for the temp directory, kept per-fixture so concurrent runs do not collide and so the
    /// directory name stays recognizable in a crash dump.
    /// </param>
    /// <param name="width">Scene frame width.</param>
    /// <param name="height">Scene frame height.</param>
    /// <param name="start">Scene start; <see langword="null"/> leaves the property default.</param>
    /// <param name="duration">Scene duration; <see langword="null"/> leaves the property default.</param>
    public SceneHistoryHarness(
        string namePrefix,
        int width = 100,
        int height = 100,
        TimeSpan? start = null,
        TimeSpan? duration = null)
    {
        BasePath = Path.Combine(Path.GetTempPath(), $"{namePrefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(BasePath);

        Scene = new Scene(width, height, string.Empty)
        {
            Uri = new Uri(Path.Combine(BasePath, "test.scene")),
        };
        if (start is { } s) Scene.Start = s;
        if (duration is { } d) Scene.Duration = d;

        _history = new HistoryHarness(Scene);
    }

    public string BasePath { get; }

    public Scene Scene { get; }

    public OperationSequenceGenerator Sequence => _history.Sequence;

    public HistoryManager History => _history.History;

    public CoreObjectOperationObserver Observer => _history.Observer;

    /// <summary>
    /// Creates an <see cref="Element"/> backed by a fresh layer file under <see cref="BasePath"/> and
    /// appends it to <see cref="Scene"/>'s children. <see cref="Element.IsEnabled"/> defaults to
    /// <see langword="true"/>, matching the engine default, so callers that never enabled it explicitly
    /// keep the same value.
    /// </summary>
    public Element AddElement(TimeSpan start, TimeSpan length, int zIndex = 0, bool isEnabled = true)
    {
        var element = new Element
        {
            Start = start,
            Length = length,
            ZIndex = zIndex,
            IsEnabled = isEnabled,
            Uri = new Uri(Path.Combine(BasePath, $"{Guid.NewGuid():N}.layer")),
        };
        Scene.Children.Add(element);
        return element;
    }

    /// <summary>
    /// Attribute-oriented overload: a fixed 1s start, 2s length element on layer 0 with a toggleable
    /// enabled flag, matching the element-attribute fixture.
    /// </summary>
    public Element AddElement(bool isEnabled = true)
        => AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0, isEnabled);

    /// <summary>
    /// Layer-oriented overload: derives <c>Start</c> from <paramref name="zIndex"/> (one second per
    /// layer) and uses a one-second length, matching the layer-service fixtures.
    /// </summary>
    public Element AddElement(int zIndex, bool isEnabled = true)
        => AddElement(TimeSpan.FromSeconds(zIndex), TimeSpan.FromSeconds(1), zIndex, isEnabled);

    public TimelineLayer AddLayer(int zIndex)
    {
        var layer = new TimelineLayer { ZIndex = zIndex };
        Scene.Layers.Add(layer);
        return layer;
    }

    public void Dispose()
    {
        _history.Dispose();
        if (Directory.Exists(BasePath)) Directory.Delete(BasePath, recursive: true);
    }
}
