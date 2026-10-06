using Beutl.Composition;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

// Presenter support shared by the editors whose value can be a presenter of another object.
internal static class PresenterEditorHelper
{
    // The expression observable stays one cold observable subscribed twice, IsPresenter first.
    public static (ReadOnlyReactivePropertySlim<bool> IsPresenter, ReadOnlyReactivePropertySlim<string?> CurrentTargetName)
        ObservePresenter<T>(IObservable<T?> value, CompositeDisposable disposables)
        where T : CoreObject
    {
        var expressionObservable = value
            .Select(v => v switch
            {
                IPresenter<T> presenter => presenter.Target.SubscribeExpressionChange()
                    .Select(exp => (presenter, exp))!,
                _ => Observable.ReturnThenNever(
                    ((IPresenter<T>?)null, (IExpression<T?>?)null))
            })
            .Switch();
        ReadOnlyReactivePropertySlim<bool> isPresenter = expressionObservable
            .Select(t => t is { Item1: not null, Item2: ReferenceExpression<T> or null })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

        ReadOnlyReactivePropertySlim<string?> currentTargetName = expressionObservable
            .Select(t => t.Item2 is ReferenceExpression<T>
                ? t.Item1?.Target.GetValue(CompositionContext.Default)
                : null)
            .Select(obj => obj != null ? CoreObjectHelper.GetDisplayName(obj) : MessageStrings.PropertyUnset)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

        return (isPresenter, currentTargetName);
    }

    // Points the presenter at target, or clears it when target is null.
    public static void AssignTarget<T>(IPresenter<T> presenter, T? target)
        where T : CoreObject
    {
        if (target != null)
        {
            presenter.Target.Expression = Expression.CreateReference<T>(target.Id);
        }
        else
        {
            presenter.Target.Expression = null;
            presenter.Target.CurrentValue = null;
        }
    }
}
