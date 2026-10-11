using System.Globalization;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.Editor.Services;

// The notice an editor shows once when its scene opens with elements whose file recovery keeps
// (Element.IsRecoveryProtected). Those elements are read-only, and nothing else tells the user why.
internal static class RecoveryProtectionNotice
{
    private const int MaxListedElements = 5;

    // Null when no element of the scene is recovery-protected.
    public static Notification? Create(Scene scene, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        string[] names = scene.Children
            .Where(static element => element.IsRecoveryProtected)
            .OrderBy(static element => element.Start)
            .ThenBy(static element => element.ZIndex)
            .Select(GetLabel)
            .ToArray();
        if (names.Length == 0) return null;

        string listed = string.Join(", ", names.Take(MaxListedElements));
        if (names.Length > MaxListedElements) listed += ", …";

        return new Notification(
            MessageStrings.RecoveryProtectedElementsTitle,
            string.Format(
                CultureInfo.CurrentCulture,
                MessageStrings.RecoveryProtectedElementsMessage,
                scene.Name,
                listed),
            NotificationType.Warning,
            Expiration: Timeout.InfiniteTimeSpan)
        {
            CancellationToken = cancellationToken,
        };
    }

    // The element's name, or its file name when it has none.
    private static string GetLabel(Element element)
    {
        if (!string.IsNullOrWhiteSpace(element.Name)) return element.Name;

        return element.Uri is { } uri ? Path.GetFileNameWithoutExtension(uri.LocalPath) : string.Empty;
    }
}
