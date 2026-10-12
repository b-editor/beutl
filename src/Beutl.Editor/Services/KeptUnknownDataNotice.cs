using System.Globalization;
using Beutl.Language;
using Beutl.Serialization;
using Beutl.Services;

namespace Beutl.Editor.Services;

// The notice shown once when a project opens with data this build keeps but cannot show: members that a type
// does not read, or objects whose type could not be loaded. Usually a newer version of Beutl or an extension
// that is not installed wrote them. Saving writes them back unchanged, and nothing else tells the user they are
// there.
internal static class KeptUnknownDataNotice
{
    // Null when reading the project kept no such data.
    public static Notification? Create(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!RootFileVersions.HasKeptUnknownData(project)) return null;

        // The version that last saved the project; null when its file records none, or a value that is not a version.
        string? savedBy = RootFileVersions.GetLastSavedVersion(project);
        string message = savedBy is null
            ? MessageStrings.KeptUnknownDataMessage
            : string.Format(CultureInfo.CurrentCulture, MessageStrings.KeptUnknownDataSavedByMessage, savedBy);

        return new Notification(
            MessageStrings.KeptUnknownDataTitle,
            message,
            NotificationType.Warning,
            Expiration: Timeout.InfiniteTimeSpan);
    }
}
