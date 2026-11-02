using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl;

// Keep the expression and its diagnostics together so in-flight evaluations cannot
// change the error or logging state of a later assignment.
internal sealed class ExpressionEvaluationState<T>(IExpression<T> expression)
{
    private string? _error;
    private int _reported;

    public IExpression<T> Expression { get; } = expression;

    public string? Error => Volatile.Read(ref _error);

    public void ClearError() => Volatile.Write(ref _error, null);

    // A script can return NaN or infinity without throwing, as a division by zero does. Neither is a value a
    // property can mean, and a range validator would turn infinity into a bound that looks valid.
    public static T RequireFinite(T value)
    {
        if (value is float single && !float.IsFinite(single) || value is double number && !double.IsFinite(number))
        {
            throw new ExpressionException($"The expression returned {value}, which is not a finite number.");
        }

        return value;
    }

    public void ReportFailure(string? propertyName, CoreObject? owner, ExpressionException exception)
    {
        Volatile.Write(ref _error, exception.Message);
        if (Interlocked.Exchange(ref _reported, 1) == 0)
        {
            Log.CreateLogger("PropertyExpression").LogWarning(exception,
                "Expression for {OwnerType} ({OwnerId}).{PropertyName} failed; using the animation or current value.",
                owner?.GetType().Name, owner?.Id.Value, propertyName);
        }
    }
}
