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
        ApplyStart(scene, pointerTime, initialStart, initialDuration);
    }

    public void UpdateEndDrag(Scene scene, TimeSpan pointerTime)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ApplyEnd(scene, pointerTime);
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
}
