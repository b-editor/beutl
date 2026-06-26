using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.ProjectSystem;

namespace Beutl.E2ETests.TestInfrastructure;

// Standalone copy of the wiring in tests/Beutl.UnitTests/TestInfrastructure/SceneHistoryHarness.cs;
// the E2E project must not reference the Beutl.UnitTests assembly.
/// <summary>
/// Scene-rooted harness wiring an on-disk <see cref="Scene"/>, an
/// <see cref="OperationSequenceGenerator"/>, a <see cref="HistoryManager"/>, and a subscribed
/// <see cref="CoreObjectOperationObserver"/> so property mutations accumulate until
/// <see cref="HistoryManager.Commit"/>.
/// </summary>
public sealed class SceneHistoryHarness : IDisposable
{
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

        Sequence = new OperationSequenceGenerator();
        History = new HistoryManager(Scene, Sequence);
        Observer = new CoreObjectOperationObserver(null, Scene, Sequence);
        History.Subscribe(Observer);
    }

    public string BasePath { get; }

    public Scene Scene { get; }

    public OperationSequenceGenerator Sequence { get; }

    public HistoryManager History { get; }

    public CoreObjectOperationObserver Observer { get; }

    /// <summary>
    /// Creates an <see cref="Element"/> backed by a fresh <c>.belm</c> file under
    /// <see cref="BasePath"/> and appends it to <see cref="Scene"/>'s children. Uses the
    /// <c>.belm</c> extension so the scene's default include matcher round-trips it from disk.
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

    public void Dispose()
    {
        Observer.Dispose();
        History.Dispose();
        if (Directory.Exists(BasePath)) Directory.Delete(BasePath, recursive: true);
    }
}
