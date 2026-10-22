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

    public void SetStart(Scene scene, MediaTime newStart)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ApplyStart(scene, newStart);
        _historyManager.Commit(CommandNames.ChangeSceneStart);
    }

    public void SetEnd(Scene scene, MediaTime newEnd)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ApplyEnd(scene, newEnd);
        _historyManager.Commit(CommandNames.ChangeSceneDuration);
    }

    public void UpdateStartDrag(Scene scene, MediaTime pointerTime, MediaTime initialStart, MediaTime initialDuration)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ApplyStartDrag(scene, pointerTime, initialStart, initialDuration);
    }

    public void UpdateEndDrag(Scene scene, MediaTime pointerTime)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ApplyEndDrag(scene, pointerTime);
    }

    internal static Rational GetFrameRate(Scene scene) => scene.FindHierarchicalParent<Project>().GetFrameRate();

    internal static MediaTime GetFrameDuration(Scene scene) => MediaTime.FromFrames(1, GetFrameRate(scene));

    /// <summary>
    /// One-shot start update (keyboard / menu path). Setting start past the
    /// current end shifts the end forward one frame and snaps start to the old
    /// end.
    /// </summary>
    private static void ApplyStart(Scene scene, MediaTime newStart)
    {
        MediaTime frame = GetFrameDuration(scene);
        MediaTime sceneEnd = scene.Start + scene.Duration;

        if (newStart > sceneEnd)
        {
            // Start past end: shift end forward one frame, snap start to old end.
            MediaTime shift = newStart - sceneEnd + frame;
            scene.Duration = shift;
            scene.Start = sceneEnd;
            return;
        }

        if (newStart < MediaTime.Zero)
        {
            newStart = MediaTime.Zero;
        }
        else if (newStart > sceneEnd - frame)
        {
            newStart = sceneEnd - frame;
        }

        MediaTime newDuration = sceneEnd - newStart > frame ? sceneEnd - newStart : frame;
        scene.Duration = newDuration;
        scene.Start = newStart;
    }

    /// <summary>
    /// One-shot end update (keyboard / menu path). An end before the current
    /// start shifts both start and duration backward.
    /// </summary>
    private static void ApplyEnd(Scene scene, MediaTime newEnd)
    {
        MediaTime frame = GetFrameDuration(scene);

        if (newEnd < scene.Start)
        {
            // End dragged before the start: shift both back one frame.
            MediaTime shifted = newEnd - frame;
            if (shifted < MediaTime.Zero) shifted = MediaTime.Zero;
            scene.Duration = scene.Start - shifted;
            scene.Start = shifted;
            return;
        }

        MediaTime duration = newEnd - scene.Start;
        if (duration <= MediaTime.Zero) duration = frame;
        scene.Duration = duration;
    }

    /// <summary>
    /// Drag-phase start update. Clamps start to <c>[0, initialEnd - 1 frame]</c>
    /// against the end captured at drag-press, pinning the scene end. Unlike
    /// <see cref="ApplyStart"/> it never shifts the end forward (drag past end
    /// clamps to the end).
    /// </summary>
    private static void ApplyStartDrag(Scene scene, MediaTime newStart, MediaTime initialStart, MediaTime initialDuration)
    {
        MediaTime frame = GetFrameDuration(scene);
        MediaTime sceneEnd = initialStart + initialDuration;

        if (newStart < MediaTime.Zero) newStart = MediaTime.Zero;
        if (newStart > sceneEnd - frame) newStart = sceneEnd - frame;

        scene.Start = newStart;
        scene.Duration = sceneEnd - newStart;
    }

    /// <summary>
    /// Drag-phase end update. Clamps duration to <c>>= 1 frame</c> and never
    /// touches <see cref="Scene.Start"/>; dragging left of Start must only
    /// shrink duration, not move the scene backward.
    /// </summary>
    private static void ApplyEndDrag(Scene scene, MediaTime pointerTime)
    {
        MediaTime frame = GetFrameDuration(scene);

        MediaTime duration = pointerTime - scene.Start;
        if (duration < frame) duration = frame;
        scene.Duration = duration;
    }
}
