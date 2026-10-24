using Beutl.Composition;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;
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
            .Select(t =>
            {
                T? target = _reference.Evaluate(new CompositionContext(t.Second));
                IObservable<T?> targets = Observable.ReturnThenNever(target);
                if (target is null && _reference.Expression is IReferenceExpression reference
                    && _reference.GetOwnerObject() is IHierarchical { HierarchicalRoot: { } root })
                {
                    targets = Observable.FromEventPattern<IHierarchical>(
                            handler => root.DescendantAttached += handler,
                            handler => root.DescendantAttached -= handler)
                        .Where(e => e.EventArgs is T added && added.Id.Value == reference.ObjectId)
                        .Select(_ => _reference.Evaluate(new CompositionContext(t.Second)))
                        .Where(current => current is not null)
                        .Take(1)
                        .StartWith(target);
                }

                return targets.Select(current => (Target: current, Time: t.Second));
            })
            .Switch()
            .Select(t =>
            {
                T? target = t.Target;
                if (target == null) return Observable.ReturnThenNever<string?>(MessageStrings.PropertyUnset);

                var changes = target.Name.GetObservable();
                if (target is IHierarchical hierarchical)
                {
                    changes = changes.Merge(Observable.FromEventPattern<HierarchyAttachmentEventArgs>(
                        handler =>
                        {
                            hierarchical.AttachedToHierarchy += handler;
                            hierarchical.DetachedFromHierarchy += handler;
                        },
                        handler =>
                        {
                            hierarchical.AttachedToHierarchy -= handler;
                            hierarchical.DetachedFromHierarchy -= handler;
                        }).Select(_ => target.Name.Value));
                }

                return changes.Select(_ => _reference.Evaluate(new CompositionContext(t.Time)) is { } current
                    ? (string?)GetDisplayName(current)
                    : MessageStrings.PropertyUnset);
            })
            .Switch()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> CurrentTargetName { get; }

    public void SetTarget(CoreObject? target)
    {
        if (IsDisposed || !CanEditProperty.Value
            || target is not null && (target is not T || CreatesReferenceCycle(target))) return;

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
                ? [.. project.Items.OfType<Scene>()
                    .Where(scene => !CreatesReferenceCycle(scene))
                    .Select(scene => new TargetObjectInfo(GetDisplayName(scene), scene))]
                : [];
        }

        return [.. TargetObjectSearchHelper.GetAvailableTargets<T>(this)
            .Where(target => !CreatesReferenceCycle(target.Object))
            .Select(target => new TargetObjectInfo(GetDisplayName(target.Object), target.Object))];
    }

    private bool CreatesReferenceCycle(CoreObject target)
    {
        CoreObject? owner = _reference.GetOwnerObject();
        var currentScene = target is Scene
            ? (owner as IHierarchical)?.FindHierarchicalParent<Scene>() ?? this.GetService<Scene>()
            : null;

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>();
        var context = new CompositionContext(this.GetService<IEditorClock>()?.CurrentTime.Value ?? MediaTime.Zero);
        pending.Push(target);
        while (pending.TryPop(out object? current))
        {
            if (ReferenceEquals(current, owner) || ReferenceEquals(current, currentScene)) return true;
            if (!visited.Add(current)) continue;

            foreach (object child in ChildObjects.Of(current))
                pending.Push(child);

            if (current is IExpressionProperty { IsReference: true } property)
            {
                CoreObject? referenced = property switch
                {
                    IExpressionProperty<T?> typed => typed.Evaluate(context),
                    IExpressionProperty<Scene?> scene => scene.Evaluate(context),
                    IExpressionProperty<Drawable?> drawable => drawable.Evaluate(context),
                    IExpressionProperty<FilterEffect?> effect => effect.Evaluate(context),
                    IExpressionProperty<Transform?> transform => transform.Evaluate(context),
                    IExpressionProperty<Brush?> brush => brush.Evaluate(context),
                    _ => null
                };
                if (referenced != null)
                    pending.Push(referenced);
            }
        }

        return false;
    }

    private static string GetDisplayName(CoreObject target)
        => string.IsNullOrEmpty(target.Name.Value) ? CoreObjectHelper.GetDisplayName(target) : target.Name.Value;
}
