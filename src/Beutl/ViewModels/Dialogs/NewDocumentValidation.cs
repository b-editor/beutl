using Avalonia;

namespace Beutl.ViewModels.Dialogs;

/// <summary>
/// The checks the new project and new scene dialogs share: a folder name that can still be
/// created where it goes, a frame that has a size, positive rates, and the first free default name.
/// </summary>
internal static class NewDocumentValidation
{
    public static string? ValidateName(string? name, string location)
    {
        if (string.IsNullOrEmpty(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) > -1)
        {
            return MessageStrings.InvalidString;
        }
        else if (Directory.Exists(Path.Combine(location, name)))
        {
            return MessageStrings.AlreadyExists;
        }
        else
        {
            return null;
        }
    }

    public static string? ValidateSize(PixelSize size)
        => size.Width <= 0 || size.Height <= 0
            ? MessageStrings.ValueLessThanOrEqualToZero
            : null;

    public static string? ValidatePositive(int value)
        => value <= 0 ? MessageStrings.ValueLessThanOrEqualToZero : null;

    // Whether the folder can be created and the frame has a size. Nothing can be created
    // without a name and a location.
    public static bool CanCreateAt(string? name, string? location, PixelSize size)
        => location != null
            && name != null
            && !Directory.Exists(Path.Combine(location, name))
            && size.Width > 0
            && size.Height > 0;

    // The first of baseName1, baseName2, ... that is not a folder in location yet.
    public static string UniqueName(string location, string baseName)
    {
        int n = 1;

        while (Directory.Exists(Path.Combine(location, baseName + n)))
        {
            n++;
        }

        return baseName + n;
    }
}
