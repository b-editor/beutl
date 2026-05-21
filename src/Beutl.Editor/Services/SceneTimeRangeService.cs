using Beutl.Language;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed class SceneTimeRangeService : ISceneTimeRangeService
{
    private readonly HistoryManager _historyManager;

    public SceneTimeRangeService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public void SetStart(Scene scene, TimeSpan newStart)
    {
        ArgumentNullException.ThrowIfNull(scene);

        int rate = GetFrameRate(scene);
        TimeSpan frame = TimeSpan.FromSeconds(1d / rate);
        TimeSpan sceneEnd = scene.Start + scene.Duration;

        if (newStart > sceneEnd)
        {
            // Moving the start past the current end: shift the scene end forward by
            // one frame and snap start to the old end.
            TimeSpan shift = newStart - sceneEnd + frame;
            scene.Duration = shift;
            scene.Start = sceneEnd;
        }
        else
        {
            if (newStart < TimeSpan.Zero)
            {
                newStart = TimeSpan.Zero;
            }
            else if (newStart > sceneEnd - frame)
            {
                newStart = sceneEnd - frame;
            }

            TimeSpan newDuration = TimeSpan.FromTicks(Math.Max((sceneEnd - newStart).Ticks, frame.Ticks));
            scene.Duration = newDuration;
            scene.Start = newStart;
        }

        _historyManager.Commit(CommandNames.ChangeSceneStart);
    }

    public void SetEnd(Scene scene, TimeSpan newEnd)
    {
        ArgumentNullException.ThrowIfNull(scene);

        int rate = GetFrameRate(scene);
        TimeSpan frame = TimeSpan.FromSeconds(1d / rate);

        if (newEnd < scene.Start)
        {
            // Pulling the end past the current start: shift both back by one frame.
            TimeSpan shifted = newEnd - frame;
            if (shifted < TimeSpan.Zero) shifted = TimeSpan.Zero;
            scene.Duration = scene.Start - shifted;
            scene.Start = shifted;
        }
        else
        {
            TimeSpan duration = newEnd - scene.Start;
            if (duration <= TimeSpan.Zero) duration = frame;
            scene.Duration = duration;
        }

        _historyManager.Commit(CommandNames.ChangeSceneDuration);
    }

    public ISceneTimeRangeDragSession BeginDragStart(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return new DragSession(_historyManager, scene, DragMode.Start);
    }

    public ISceneTimeRangeDragSession BeginDragEnd(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return new DragSession(_historyManager, scene, DragMode.End);
    }

    internal static int GetFrameRate(Scene scene)
    {
        Project? project = scene.FindHierarchicalParent<Project>();
        if (project is null) return 30;
        return project.Variables.TryGetValue(ProjectVariableKeys.FrameRate, out string? value)
            && int.TryParse(value, out int rate)
            ? rate
            : 30;
    }

    private enum DragMode { Start, End }

    private sealed class DragSession : ISceneTimeRangeDragSession
    {
        private readonly HistoryManager _historyManager;
        private readonly Scene _scene;
        private readonly DragMode _mode;
        private readonly TimeSpan _initialStart;
        private readonly TimeSpan _initialDuration;
        private readonly int _rate;
        private bool _disposed;
        private bool _settled;

        public DragSession(HistoryManager historyManager, Scene scene, DragMode mode)
        {
            _historyManager = historyManager;
            _scene = scene;
            _mode = mode;
            _initialStart = scene.Start;
            _initialDuration = scene.Duration;
            _rate = GetFrameRate(scene);
        }

        public void Update(TimeSpan pointerFrame)
        {
            if (_disposed || _settled) return;
            if (pointerFrame < TimeSpan.Zero) pointerFrame = TimeSpan.Zero;

            TimeSpan frame = TimeSpan.FromSeconds(1d / _rate);

            if (_mode == DragMode.End)
            {
                TimeSpan newDuration = pointerFrame - _scene.Start;
                if (newDuration < TimeSpan.Zero) newDuration = frame;
                _scene.Duration = newDuration;
            }
            else
            {
                TimeSpan clampedStart = pointerFrame;
                TimeSpan totalEnd = _initialStart + _initialDuration;
                if (clampedStart > totalEnd - frame) clampedStart = totalEnd - frame;

                _scene.Start = clampedStart;
                _scene.Duration = totalEnd - clampedStart;
            }
        }

        public void Commit()
        {
            if (_disposed || _settled) return;
            _settled = true;
            string name = _mode == DragMode.End ? CommandNames.ChangeSceneDuration : CommandNames.ChangeSceneStart;
            _historyManager.Commit(name);
        }

        public void Cancel()
        {
            if (_disposed || _settled) return;
            _settled = true;
            _scene.Start = _initialStart;
            _scene.Duration = _initialDuration;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
