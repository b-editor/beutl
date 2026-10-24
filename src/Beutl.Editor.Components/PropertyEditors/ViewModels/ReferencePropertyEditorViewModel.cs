using Beutl.Composition;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.PropertyEditors;
using Beutl.Engine.Expressions;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public interface IReferencePropertyEditorViewModel
{
    bool IsDisposed { get; }

    string Header { get; }

    ReactivePropertySlim<string?> HoverInfo { get; }

    ReadOnlyReactivePropertySlim<bool> CanEditProperty { get; }

    ReadOnlyReactivePropertySlim<string?> CurrentTargetName { get; }

    IReadOnlyList<TargetObjectInfo> GetAvailableTargets();

    void SetTarget(CoreObject? target);
}

public sealed class ReferencePropertyEditorViewModel<T> : PropertyEditorViewModel<T?>, IReferencePropertyEditorViewModel
    where T : CoreObject
{
    private readonly IExpressionProperty<T?> _reference;

    public ReferencePropertyEditorViewModel(IPropertyAdapter<T?> property)
        : base(property)
    {
        _reference = (IExpressionProperty<T?>)property.GetEngineProperty()!;
        CurrentTargetName = property.GetObservable()
            .CombineLatest(_reference.SubscribeExpressionChange())
            .CombineLatest(CurrentTime)
            .Select(t => _reference.Evaluate(new CompositionContext(t.Second)))
            .Select(target => target == null
                ? Observable.ReturnThenNever<string?>(MessageStrings.PropertyUnset)
                : target.Name.GetObservable().Select(_ => (string?)GetDisplayName(target)))
            .Switch()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> CurrentTargetName { get; }

    public void SetTarget(CoreObject? target)
    {
        if (IsDisposed || !CanEditProperty.Value
            || target is not null && (target is not T || ReferenceEquals(target, _reference.GetOwnerObject()))) return;

        PropertyAdapter.SetValue(null);
        _reference.Expression = target is T typed ? new ReferenceExpression<T?>(typed.Id.Value) : null;
        Commit();
    }

    public IReadOnlyList<TargetObjectInfo> GetAvailableTargets()
    {
        if (IsDisposed) return [];
        if (typeof(T) == typeof(Scene))
        {
            return this.GetService<Scene>()?.FindHierarchicalParent<Project>() is { } project
                ? [.. project.Items.OfType<Scene>().Select(scene => new TargetObjectInfo(GetDisplayName(scene), scene))]
                : [];
        }

        return [.. TargetObjectSearchHelper.GetAvailableTargets<T>(this)
            .Where(target => !ReferenceEquals(target.Object, _reference.GetOwnerObject()))
            .Select(target => new TargetObjectInfo(GetDisplayName(target.Object), target.Object))];
    }

    private static string GetDisplayName(CoreObject target)
        => string.IsNullOrEmpty(target.Name.Value) ? CoreObjectHelper.GetDisplayName(target) : target.Name.Value;
}
