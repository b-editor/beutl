using System.Reactive;

namespace Beutl.Editor.Components.PropertyEditors.Services;

// The root of objects that stand for objects of several others, as the copy the element property tab shows for several
// elements does: the editors show of them only what each of the others holds too, so that an edit reaches all of them.
// An editor finds it as the hierarchical root of what it edits.
internal interface IPropertyEditorScope
{
    // Raised when what the scope shows can have changed.
    IObservable<Unit> Changed { get; }

    // Whether an editor shows property of owner.
    bool Shows(CoreObject owner, IProperty property);

    // Whether a list shows obj, one of its items.
    bool Shows(CoreObject obj);

    // The types of the objects obj stands for, its own first.
    IReadOnlyList<Type> TypesOf(CoreObject obj);
}

internal static class PropertyEditorScope
{
    public static IPropertyEditorScope? Of(object? obj) => (obj as IHierarchical)?.HierarchicalRoot as IPropertyEditorScope;

    // Whether an editor of adapter is shown: always, unless its property belongs to a scope that hides it.
    public static bool Shows(IPropertyAdapter adapter)
        => adapter.GetEngineProperty() is not { } property
           || property.GetOwnerObject() is not { } owner
           || Of(owner)?.Shows(owner, property) != false;

    // Leaves out of adapters those whose editors are not shown.
    public static void RemoveHidden(IList<IPropertyAdapter> adapters)
    {
        for (int i = adapters.Count - 1; i >= 0; i--)
        {
            if (!Shows(adapters[i])) adapters.RemoveAt(i);
        }
    }

    // Whether a list shows obj, as what its scope shows changes.
    public static IObservable<bool> ObserveShows(object? obj)
        => obj is CoreObject coreObject && Of(coreObject) is { } scope
            ? scope.Changed.StartWith(Unit.Default).Select(_ => scope.Shows(coreObject)).DistinctUntilChanged()
            : Observable.ReturnThenNever(true);

    // The name of what obj stands for: the names of the types of the objects, when they are not all of one.
    public static string? DescribeTypes(CoreObject obj)
        => Of(obj)?.TypesOf(obj) is { Count: > 1 } types
            ? string.Join(" / ", types.Select(TypeDisplayHelpers.GetLocalizedName))
            : null;
}
