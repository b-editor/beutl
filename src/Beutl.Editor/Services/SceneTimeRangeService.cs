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
    /// One-shot start update (keyboard / menu path). Setting start past the
    /// current end shifts the end forward one frame and snaps start to the old
    /// end, preserving the legacy "set start to pointer position" behavior.
    /// </summary>
    private static void ApplyStart(Scene scene, TimeSpan newStart, TimeSpan referenceStart, TimeSpan referenceDuration)
    {
        int rate = GetFrameRate(scene);
        TimeSpan frame = TimeSpan.FromSeconds(1d / rate);
        TimeSpan sceneEnd = referenceStart + referenceDuration;

        if (newStart > sceneEnd)
        {
            // Start past end: shift end forward one frame, snap start to old end.
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
    /// One-shot end update (keyboard / menu path). An end before the current
    /// start shifts both start and duration backward, preserving the legacy
    /// "set end to pointer position" behavior.
    /// </summary>
    private static void ApplyEnd(Scene scene, TimeSpan newEnd)
    {
        int rate = GetFrameRate(scene);
        TimeSpan frame = TimeSpan.FromSeconds(1d / rate);

        if (newEnd < scene.Start)
        {
            // End dragged before the start: shift both back one frame.
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
    /// Drag-phase start update. Clamps start to <c>[0, initialEnd - 1 frame]</c>
    /// against the end captured at drag-press, pinning the scene end. Unlike
    /// <see cref="ApplyStart"/> it never shifts the end forward (drag past end
    /// clamps to the end), matching the pre-refactor drag loop.
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
    /// Drag-phase end update. Clamps duration to <c>>= 1 frame</c> and never
    /// touches <see cref="Scene.Start"/>; dragging left of Start must only
    /// shrink duration, not move the scene backward — a regression vs. the
    /// pre-refactor drag loop.
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
