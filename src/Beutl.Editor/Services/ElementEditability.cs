using System.Collections.Specialized;
using System.Reactive;
using System.Reactive.Linq;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

internal static class ElementEditability
{
    // False while the element or its layer is locked, or while recovery keeps the element's
    // original file (Element.IsRecoveryProtected), because saving would not write the edit.
    public static bool IsEditable(Element? element, Scene? scene = null)
    {
        if (element is null) return true;

        scene ??= element.FindHierarchicalParent<Scene>();
        return !(scene?.IsElementLocked(element) ?? (element.IsLocked || element.IsRecoveryProtected));
    }

    // Whether an edit removing or replacing content of the element is allowed: besides an editable element, one that
    // only recovery protects when the content holds what could not be loaded, or when nothing unloadable is left.
    public static bool CanRepair(Element? element, object? content, Scene? scene = null)
    {
        if (IsEditable(element, scene)) return true;

        scene ??= element!.FindHierarchicalParent<Scene>();
        return element!.IsRecoveryProtected
               && !(scene?.IsElementLockedByUser(element) ?? element.IsLocked)
               && (Scene.HoldsRecoveryBlocker(content) || !Scene.HasUnresolvedRecoveryBlocker(element));
    }

    public static IObservable<bool> Observe(Element? element, Scene? scene = null)
    {
        if (element is null) return Observable.Return(true);

        scene ??= element.FindHierarchicalParent<Scene>();
        Scene? owner = scene;
        return ObserveChanges(element, owner)
            .Select(_ => IsEditable(element, owner))
            .DistinctUntilChanged();
    }

    // Like Observe, for edits that remove or replace content (see CanRepair). It is checked again when
    // a lock or the protection changes, not after edits inside the content; callers that act on it
    // still go through a service that checks CanRepair at that moment.
    public static IObservable<bool> ObserveRepair(Element? element, object? content, Scene? scene = null)
    {
        if (element is null) return Observable.Return(true);

        scene ??= element.FindHierarchicalParent<Scene>();
        Scene? owner = scene;
        return ObserveChanges(element, owner)
            .Select(_ => CanRepair(element, content, owner))
            .DistinctUntilChanged();
    }

    // Fires on subscription and whenever the element's lock, its recovery protection, or the lock
    // of the layer it is on may have changed.
    private static IObservable<Unit> ObserveChanges(Element element, Scene? scene)
    {
        IObservable<Unit> elementChanges = element.GetObservable(Element.IsLockedProperty)
            .Select(_ => Unit.Default)
            .Merge(Observable.FromEventPattern(
                    handler => element.RecoveryProtectionChanged += handler,
                    handler => element.RecoveryProtectionChanged -= handler)
                .Select(_ => Unit.Default));
        if (scene is null) return elementChanges;

        Scene owner = scene;
        // Rebind after layer membership changes, including undo/redo. A layer can move
        // onto the clip's row without either lock flag changing.
        var layerChanges = Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                handler => owner.Layers.CollectionChanged += handler,
                handler => owner.Layers.CollectionChanged -= handler)
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default)
            // Combine the initial layer values before checking the lock, instead of
            // scanning every layer for each of the initial property notifications.
            .Select(_ => owner.Layers.Count == 0
                ? Observable.Return(Unit.Default)
                : owner.Layers
                    .Select(layer => layer.GetObservable(TimelineLayer.IsLockedProperty)
                        .CombineLatest(layer.GetObservable(TimelineLayer.ZIndexProperty), (_, _) => Unit.Default))
                    .CombineLatest()
                    .Select(_ => Unit.Default))
            .Switch();

        return elementChanges
            .Merge(element.GetObservable(Element.ZIndexProperty).Select(_ => Unit.Default))
            .CombineLatest(layerChanges, (_, _) => Unit.Default);
    }
}
