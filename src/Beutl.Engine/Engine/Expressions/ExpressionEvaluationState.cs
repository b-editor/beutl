using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Engine.Expressions;

// Keep the expression and its diagnostics together so in-flight evaluations cannot
// change the error or logging state of a later assignment.
internal sealed class ExpressionEvaluationState<T>(IExpression<T> expression)
{
    private string? _error;
    private int _reported;

    public IExpression<T> Expression { get; } = expression;

    public string? Error => Volatile.Read(ref _error);

    public void ClearError() => Volatile.Write(ref _error, null);

    public void ReportFailure(string? propertyName, EngineObject? owner, ExpressionException exception)
    {
        Volatile.Write(ref _error, exception.Message);
        if (Interlocked.Exchange(ref _reported, 1) == 0)
        {
            try
            {
                Log.CreateLogger("PropertyExpression").LogWarning(exception,
                    "Expression for {OwnerType} ({OwnerId}).{PropertyName} failed; using the animation or current value.",
                    owner?.GetType().Name, owner?.Id, propertyName);
            }
            catch
            {
                // Diagnostics must not prevent the property from falling back to a usable value.
            }
        }
    }
}
