namespace Beutl;

// The time a property or an expression is evaluated at. CompositionContext and ExpressionContext add the rest.
public class EvaluationContext(TimeSpan time)
{
    public TimeSpan Time { get; set; } = time;
}
