using System.Diagnostics.CodeAnalysis;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Services;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;

namespace Beutl.Editor.PropertyEditors;

// Writes a linked expression after the editor that began the link is disposed, as the element panel does when it
// shows another element. It parses and commits as the editor would, so the link is one undoable edit.
internal abstract class PropertyLinkExpressionWriter
{
    public abstract bool TrySet(string expressionString, [NotNullWhen(false)] out string? error);
}

internal sealed class PropertyLinkExpressionWriter<TValue> : PropertyLinkExpressionWriter
{
    private readonly IExpressionPropertyAdapter<TValue> _adapter;
    private readonly IExpression<TValue>? _current;
    private readonly Element? _element;
    private readonly Scene? _scene;
    private readonly HistoryManager? _history;

    public PropertyLinkExpressionWriter(
        IExpressionPropertyAdapter<TValue> adapter, Element? element, Scene? scene, HistoryManager? history)
    {
        _adapter = adapter;
        _current = adapter.Expression;
        _element = element;
        _scene = scene;
        _history = history;
    }

    public override bool TrySet(string expressionString, [NotNullWhen(false)] out string? error)
    {
        if (!CanSetExpression(out error)) return false;
        if (!TryParseExpression(_current, expressionString, out IExpression<TValue>? expression, out error))
            return false;

        _adapter.Expression = expression;
        // An expression can replace a value that loading dropped.
        if (_element is { } element)
            ElementRecoveryService.TryCompleteRepair(element, _history!);
        _history!.Commit(CommandNames.EditProperty);
        error = null;
        return true;
    }

    // The check the editor makes before it writes an expression, over the state it had when the link began.
    private bool CanSetExpression([NotNullWhen(false)] out string? error)
    {
        if (_history is null)
        {
            error = MessageStrings.OperationFailed;
            return false;
        }

        // CanReplace(null, [value, animation]): the kept value and animation must not still hold what could not be
        // loaded, or the element stays protected and saving drops the link.
        object? animation = (_adapter as IAnimatablePropertyAdapter)?.Animation;
        bool canReplace = ElementEditability.CanRepair(_element, null, _scene)
                          || ElementEditability.CanRepairDroppedValue(_element, _adapter.GetEngineProperty(), _scene)
                          || (_adapter is EnginePropertyAdapter<Easing> { Object: KeyFrame keyFrame }
                              && ElementEditability.CanRepair(_element, keyFrame, _scene));
        if (canReplace
            && !(_element is { IsRecoveryProtected: true }
                 && SceneRecovery.HoldsRecoveryBlocker(new object?[] { _adapter.GetValue(), animation })))
        {
            error = null;
            return true;
        }

        error = MessageStrings.OperationFailed;
        return false;
    }

    private static bool TryParseExpression(
        IExpression<TValue>? current,
        string expressionString,
        [NotNullWhen(true)] out IExpression<TValue>? expression,
        [NotNullWhen(false)] out string? error)
    {
        for (IExpression? expr = current; expr != null; expr = expr.Replaced)
        {
            if (expr is ICombinedValueExpression<TValue> combined)
            {
                return combined.TryParseSameContext(expressionString, out expression, out error);
            }
        }

        if (Expression.TryParse<TValue>(expressionString, out StringExpression<TValue>? plain, out error))
        {
            expression = plain;
            return true;
        }

        expression = null;
        return false;
    }
}
