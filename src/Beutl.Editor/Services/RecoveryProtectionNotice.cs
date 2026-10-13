using System.Globalization;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.Editor.Services;

// The notice an editor shows once when its scene opens with elements whose file recovery keeps
// (Element.IsRecoveryProtected). Those elements are read-only, and nothing else tells the user why.
internal static class RecoveryProtectionNotice
{
    // Null when no element of the scene is recovery-protected.
    public static Notification? Create(Scene scene, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (ElementNoticeFormatting.FormatNames(scene.Children
                .Where(static element => element.IsRecoveryProtected)
                .OrderBy(static element => element.Start)
                .ThenBy(static element => element.ZIndex)) is not { } listed)
        {
            return null;
        }

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
}
