using System.Globalization;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.Editor.Services;

// The notice an editor shows once when its scene opens with elements whose file recovery keeps
// (Element.IsRecoveryProtected). Those elements are read-only until repaired.
internal static class RecoveryProtectionNotice
{
    // Null when no element of the scene is recovery-protected.
    public static Notification? Create(Scene scene, CancellationToken cancellationToken = default, Action? showDetails = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (ElementNoticeFormatting.FormatNames(scene.Children
                .Where(static element => element.IsRecoveryProtected)
                .OrderBy(static element => element.Start.Value)
                .ThenBy(static element => element.ZIndex.Value)) is not { } listed)
        {
            return null;
        }

        return new Notification(
            MessageStrings.RecoveryProtectedElementsTitle,
            string.Format(
                CultureInfo.CurrentCulture,
                MessageStrings.RecoveryProtectedElementsMessage,
                scene.Name.Value,
                listed),
            NotificationType.Warning,
            Expiration: Timeout.InfiniteTimeSpan,
            Actions: showDetails is null ? null : [new(UndisplayableDataStrings.ShowDetails, showDetails)])
        {
            CancellationToken = cancellationToken,
        };
    }
}
