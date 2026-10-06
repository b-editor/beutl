using System.Collections.Specialized;
using System.Reactive;
using System.Reactive.Linq;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

internal static class ElementEditability
{
    public static bool IsEditable(Element? element, Scene? scene = null)
    {
        if (element is null) return true;

        scene ??= element.FindHierarchicalParent<Scene>();
        return !(scene?.IsElementLocked(element) ?? element.IsLocked);
    }

    public static IObservable<bool> Observe(Element? element, Scene? scene = null)
    {
        if (element is null) return Observable.Return(true);

        scene ??= element.FindHierarchicalParent<Scene>();
        var elementChanges = element.GetObservable(Element.IsLockedProperty).Select(_ => Unit.Default);
        if (scene is null)
            return elementChanges.Select(_ => IsEditable(element)).DistinctUntilChanged();

        Scene owner = scene;
        // Rebind after layer membership changes, including undo/redo. A layer can move
        // onto the clip's row without either lock flag changing.
        var layerChanges = Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                handler => owner.Layers.CollectionChanged += handler,
                handler => owner.Layers.CollectionChanged -= handler)
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default)
            .Select(_ => owner.Layers
                .Select(layer => layer.GetObservable(TimelineLayer.IsLockedProperty).Select(_ => Unit.Default)
                    .Merge(layer.GetObservable(TimelineLayer.ZIndexProperty).Select(_ => Unit.Default)))
                .Merge()
                .StartWith(Unit.Default))
            .Switch();

        return elementChanges
            .Merge(element.GetObservable(Element.ZIndexProperty).Select(_ => Unit.Default))
            .Merge(layerChanges)
            .Select(_ => IsEditable(element, owner))
            .DistinctUntilChanged();
    }
}
