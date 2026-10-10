using Beutl.Graphics.Effects;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.Helpers;

internal static class ToolTabHeaderHelper
{
    public static string Compose(string tabName, string? target)
    {
        return string.IsNullOrWhiteSpace(target)
            ? tabName
            : string.Format(CultureInfo.CurrentCulture, Strings.ToolTabHeaderFormat, tabName, target);
    }

    public static string ElementLabel(string? name, Element? element)
    {
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        string? localPath = element?.Uri?.LocalPath;
        return string.IsNullOrEmpty(localPath)
            ? string.Empty
            : Path.GetFileNameWithoutExtension(localPath);
    }

    public static IObservable<string> ObserveElementLabel(Element? element)
    {
        return element is null
            ? Observable.ReturnThenNever(string.Empty)
            : element.GetObservable(CoreObject.NameProperty).Select(n => ElementLabel(n, element));
    }

    public static IObservable<string> ObserveEffectLabel(FilterEffect? effect)
    {
        return ObserveObjectLabel(effect);
    }

    // The object's own name, or the label of the element it belongs to.
    public static IObservable<string> ObserveObjectLabel(CoreObject? obj)
    {
        if (obj is null)
            return Observable.ReturnThenNever(string.Empty);

        IObservable<string> elementLabel = ObserveElementLabel(
            (obj as IHierarchical)?.FindHierarchicalParent<Element>(includeSelf: true));

        return obj.GetObservable(CoreObject.NameProperty)
            .CombineLatest(elementLabel, (name, element) => string.IsNullOrWhiteSpace(name) ? element : name);
    }
}
