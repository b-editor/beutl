using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.Helpers;

/// <summary>
/// Builds the per-instance titles that let several tabs of one
/// <see cref="ToolTabExtension.CanMultiple"/> tool be told apart.
/// </summary>
internal static class ToolTabHeaderHelper
{
    public static string Compose(string tabName, string? target)
    {
        return string.IsNullOrWhiteSpace(target)
            ? tabName
            : string.Format(CultureInfo.CurrentCulture, Strings.ToolTabHeaderFormat, tabName, target);
    }

    // An element that was never renamed carries an empty Name, so fall back to its file name.
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
}
