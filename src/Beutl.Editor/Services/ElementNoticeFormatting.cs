using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

// The element list of a notice: up to five labels separated by commas.
internal static class ElementNoticeFormatting
{
    private const int MaxListedElements = 5;

    // Null when there are no elements.
    public static string? FormatNames(IEnumerable<Element> elements)
    {
        string[] names = elements.Select(GetLabel).ToArray();
        if (names.Length == 0) return null;

        string listed = string.Join(", ", names.Take(MaxListedElements));
        if (names.Length > MaxListedElements) listed += ", …";
        return listed;
    }

    // The element's name, or its file name when it has none.
    private static string GetLabel(Element element)
    {
        if (!string.IsNullOrWhiteSpace(element.Name.Value)) return element.Name.Value;

        return element.Uri is { } uri ? Path.GetFileNameWithoutExtension(uri.LocalPath) : string.Empty;
    }
}
