using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Engine.Expressions;

internal struct ExpressionEvaluationState
{
    private string? _error;
    private int _reported;

    public string? Error => Volatile.Read(ref _error);

    public void ClearError() => Volatile.Write(ref _error, null);

    public void ReportFailure(string? propertyName, EngineObject? owner, ExpressionException exception)
    {
        Volatile.Write(ref _error, exception.Message);
        if (Interlocked.Exchange(ref _reported, 1) == 0)
        {
            Log.CreateLogger("PropertyExpression").LogWarning(exception,
                "Expression for {OwnerType} ({OwnerId}).{PropertyName} failed; using the animation or current value.",
                owner?.GetType().Name, owner?.Id, propertyName);
        }
    }
}
