using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.TestInfrastructure;

/// <summary>
/// Wires <see cref="OperationSequenceGenerator"/>, <see cref="HistoryManager"/> and a subscribed
/// <see cref="CoreObjectOperationObserver"/> for any <see cref="CoreObject"/> root.
/// </summary>
public sealed class HistoryHarness : IDisposable
{
    public HistoryHarness(CoreObject root)
    {
        Sequence = new OperationSequenceGenerator();
        History = new HistoryManager(Sequence);
        Observer = new CoreObjectOperationObserver(null, root, Sequence);
        History.Subscribe(Observer);
    }

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
/// Owns a temp directory holding the <see cref="Scene"/> file, and composes <see cref="HistoryHarness"/> for
/// the history wiring.
/// </summary>
public sealed class SceneHistoryHarness : IDisposable
{
    private readonly HistoryHarness _history;

    /// <param name="namePrefix">
    /// Prefix for the temp directory, kept per-fixture so concurrent runs do not collide and so the
    /// directory name stays recognizable in a crash dump.
    /// </param>
    /// <param name="start">Scene start; <see langword="null"/> leaves the property default.</param>
    /// <param name="duration">Scene duration; <see langword="null"/> leaves the property default.</param>
    public SceneHistoryHarness(
        string namePrefix,
        TimeSpan? start = null,
        TimeSpan? duration = null)
    {
        BasePath = Path.Combine(Path.GetTempPath(), $"{namePrefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(BasePath);

        Scene = new Scene(100, 100, string.Empty)
        {
            Uri = new Uri(Path.Combine(BasePath, "test.scene")),
        };
        if (start is { } s) Scene.Start = s;
        if (duration is { } d) Scene.Duration = d;

        _history = new HistoryHarness(Scene);
    }

    public string BasePath { get; }

    public Scene Scene { get; }

    public HistoryManager History => _history.History;

    /// <summary>
    /// Creates an <see cref="Element"/> backed by a fresh <c>.belm</c> file under <see cref="BasePath"/> and
    /// appends it to <see cref="Scene"/>'s children. The scene's default include matcher reads <c>.belm</c>
    /// files back from disk.
    /// </summary>
    public Element AddElement(TimeSpan start, TimeSpan length, int zIndex = 0, bool isEnabled = true)
    {
        var element = new Element
        {
            Start = start,
            Length = length,
            ZIndex = zIndex,
            IsEnabled = isEnabled,
            Uri = new Uri(Path.Combine(BasePath, $"{Guid.NewGuid():N}.belm")),
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
