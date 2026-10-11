using System.Globalization;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.Editor.Services;

// The notice shown once when a project opens with elements whose Beutl 1.x node tree was not
// migrated. Their files are kept, but saving converts the rest of the project, after which Beutl 1.x
// cannot open it, and nothing else tells the user before that happens.
internal static class UnmigratedNodeTreeNotice
{
    private const int MaxListedElements = 5;

    // Null when no element of the project holds a node tree that was not migrated.
    public static Notification? Create(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        string[] names = project.Items
            .OfType<Scene>()
            .SelectMany(static scene => scene.Children
                .Where(static element => element.Objects.Any(ElementMigration.IsUnmigratedNodeTree))
                .OrderBy(static element => element.Start)
                .ThenBy(static element => element.ZIndex))
            .Select(GetLabel)
            .ToArray();
        if (names.Length == 0) return null;

        string listed = string.Join(", ", names.Take(MaxListedElements));
        if (names.Length > MaxListedElements) listed += ", …";

        return new Notification(
            MessageStrings.UnmigratedNodeTreeTitle,
            string.Format(CultureInfo.CurrentCulture, MessageStrings.UnmigratedNodeTreeMessage, listed),
            NotificationType.Warning,
            Expiration: Timeout.InfiniteTimeSpan);
    }

    // The element's name, or its file name when it has none.
    private static string GetLabel(Element element)
    {
        if (!string.IsNullOrWhiteSpace(element.Name)) return element.Name;

        return element.Uri is { } uri ? Path.GetFileNameWithoutExtension(uri.LocalPath) : string.Empty;
    }
}
