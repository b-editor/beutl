using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.Composition;

public interface ICompositor : IDisposable
{
    /// <summary>Evaluates a normal render frame.</summary>
    CompositionFrame EvaluateGraphics(TimeSpan time);

    /// <summary>
    /// Evaluates graphics resources for the specified pull purpose. Implementations must seed the same purpose into
    /// every <see cref="CompositionContext"/> used to build the returned frame.
    /// </summary>
    CompositionFrame EvaluateGraphics(TimeSpan time, RenderPullPurpose pullPurpose)
    {
        if (pullPurpose == RenderPullPurpose.Frame)
            return EvaluateGraphics(time);

        RenderPolicyValidation.Validate(pullPurpose, nameof(pullPurpose));
        throw new NotSupportedException(
            $"{GetType().FullName} must implement purpose-aware graphics evaluation before it can service auxiliary pulls.");
    }

    /// <summary>Evaluates a normal audio frame.</summary>
    CompositionFrame EvaluateAudio(TimeRange timeRange);

    /// <summary>
    /// Evaluates audio resources for the specified pull purpose. Implementations must seed the same purpose into
    /// every <see cref="CompositionContext"/> used to build the returned frame.
    /// </summary>
    CompositionFrame EvaluateAudio(TimeRange timeRange, RenderPullPurpose pullPurpose)
    {
        if (pullPurpose == RenderPullPurpose.Frame)
            return EvaluateAudio(timeRange);

        RenderPolicyValidation.Validate(pullPurpose, nameof(pullPurpose));
        throw new NotSupportedException(
            $"{GetType().FullName} must implement purpose-aware audio evaluation before it can service auxiliary pulls.");
    }
}
