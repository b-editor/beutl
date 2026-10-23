using Beutl.Composition;
using Beutl.Engine;

namespace Beutl.Graphics.Effects;

/// <summary>The clock a script effect hands to its script: the element's duration, the local time and the progress.</summary>
internal struct ScriptTimeline
{
    public float Progress { get; private set; }

    public float Duration { get; private set; }

    public float Time { get; private set; }

    /// <summary>Reads the clock of <paramref name="obj"/> at the context's time, and returns whether it moved.</summary>
    public bool Update(EngineObject obj, CompositionContext context)
    {
        float duration = (float)obj.TimeRange.Value.Duration.ToSeconds();
        float time = (float)(context.Time - obj.TimeRange.Value.Start).ToSeconds();
        float progress = duration > 0 ? time / duration : 0;

        // ReSharper disable CompareOfFloatsByEqualityOperator
        bool changed = Duration != duration || Time != time || Progress != progress;
        // ReSharper restore CompareOfFloatsByEqualityOperator

        Duration = duration;
        Time = time;
        Progress = progress;
        return changed;
    }
}
