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
        ApplyStart(scene, newStart, scene.Start, scene.Duration);
        _historyManager.Commit(CommandNames.ChangeSceneStart);
    }

    public void SetEnd(Scene scene, TimeSpan newEnd)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ApplyEnd(scene, newEnd);
        _historyManager.Commit(CommandNames.ChangeSceneDuration);
    }

    public void UpdateStartDrag(Scene scene, TimeSpan pointerTime, TimeSpan initialStart, TimeSpan initialDuration)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ApplyStartDrag(scene, pointerTime, initialStart, initialDuration);
    }

    public void UpdateEndDrag(Scene scene, TimeSpan pointerTime)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ApplyEndDrag(scene, pointerTime);
    }

    public void CommitStartChange()
    {
        _historyManager.Commit(CommandNames.ChangeSceneStart);
    }

    public void CommitEndChange()
    {
        _historyManager.Commit(CommandNames.ChangeSceneDuration);
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

    /// <summary>
    /// One-shot start update. Used by the keyboard / menu path. When the
    /// caller asks to set start past the current end, the scene end is
    /// shifted forward by one frame and the start snaps to the old end —
    /// this preserves the historical "set start to pointer position"
    /// menu behavior.
    /// </summary>
    private static void ApplyStart(Scene scene, TimeSpan newStart, TimeSpan referenceStart, TimeSpan referenceDuration)
    {
        int rate = GetFrameRate(scene);
        TimeSpan frame = TimeSpan.FromSeconds(1d / rate);
        TimeSpan sceneEnd = referenceStart + referenceDuration;

        if (newStart > sceneEnd)
        {
            // Moving the start past the current end: shift the scene end forward
            // by one frame and snap start to the old end.
            TimeSpan shift = newStart - sceneEnd + frame;
            scene.Duration = shift;
            scene.Start = sceneEnd;
            return;
        }

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

    /// <summary>
    /// One-shot end update. Used by the keyboard / menu path. When the
    /// caller asks for an end before the current start, both start and
    /// duration shift backward — this preserves the historical "set end
    /// to pointer position" menu behavior.
    /// </summary>
    private static void ApplyEnd(Scene scene, TimeSpan newEnd)
    {
        int rate = GetFrameRate(scene);
        TimeSpan frame = TimeSpan.FromSeconds(1d / rate);

        if (newEnd < scene.Start)
        {
            // Pulling the end past the current start: shift both back by one frame.
            TimeSpan shifted = newEnd - frame;
            if (shifted < TimeSpan.Zero) shifted = TimeSpan.Zero;
            scene.Duration = scene.Start - shifted;
            scene.Start = shifted;
            return;
        }

        TimeSpan duration = newEnd - scene.Start;
        if (duration <= TimeSpan.Zero) duration = frame;
        scene.Duration = duration;
    }

    /// <summary>
    /// Drag-phase start update. Clamps the new start to
    /// <c>[0, initialEnd - 1 frame]</c> against the initial end captured
    /// at drag-press time, so the absolute end of the scene stays pinned
    /// while the user drags the start marker. Unlike <see cref="ApplyStart"/>
    /// this never shifts the end forward — dragging the start marker past
    /// the end is treated as "right up against the end", matching the
    /// pre-refactor drag-loop behavior.
    /// </summary>
    private static void ApplyStartDrag(Scene scene, TimeSpan newStart, TimeSpan initialStart, TimeSpan initialDuration)
    {
        int rate = GetFrameRate(scene);
        TimeSpan frame = TimeSpan.FromSeconds(1d / rate);
        TimeSpan sceneEnd = initialStart + initialDuration;

        if (newStart < TimeSpan.Zero) newStart = TimeSpan.Zero;
        if (newStart > sceneEnd - frame) newStart = sceneEnd - frame;

        scene.Start = newStart;
        scene.Duration = sceneEnd - newStart;
    }

    /// <summary>
    /// Drag-phase end update. Clamps duration to <c>>= 1 frame</c> but
    /// never touches <see cref="Scene.Start"/>. Pulling the pointer left
    /// of <see cref="Scene.Start"/> during a drag must not jerk the
    /// scene's absolute time range backward — that was a regression vs.
    /// the pre-refactor drag-loop, which only adjusted duration.
    /// </summary>
    private static void ApplyEndDrag(Scene scene, TimeSpan pointerTime)
    {
        int rate = GetFrameRate(scene);
        TimeSpan frame = TimeSpan.FromSeconds(1d / rate);

        TimeSpan duration = pointerTime - scene.Start;
        if (duration < frame) duration = frame;
        scene.Duration = duration;
    }
}
