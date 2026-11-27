namespace Beutl;

// The time a property or an expression is evaluated at. CompositionContext and ExpressionContext add the rest.
public class EvaluationContext(MediaTime time)
{
    public MediaTime Time { get; set; } = time;

    internal (CoreObject Owner, MediaTime Start, MediaTime Duration)? PreviewTimeRange { get; init; }

    internal (MediaTime Start, MediaTime Duration)? GetPreviewTimeRange(CoreObject? owner)
        => PreviewTimeRange is { } range && ReferenceEquals(range.Owner, owner)
            ? (range.Start, range.Duration)
            : null;
}
