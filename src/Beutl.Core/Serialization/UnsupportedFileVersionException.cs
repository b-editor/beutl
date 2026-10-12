using NuGet.Versioning;

namespace Beutl.Serialization;

/// <summary>
/// The exception thrown when a root file records a <c>minAppVersion</c> above the running version of
/// Beutl, or one it cannot compare.
/// </summary>
/// <remarks>
/// A file that needs a newer Beutl is refused whole: it is neither read in part nor kept as a fallback,
/// so a save cannot overwrite what the newer version wrote.
/// </remarks>
public sealed class UnsupportedFileVersionException : Exception
{
    public UnsupportedFileVersionException(Uri uri, string requiredVersion)
        : base(CreateMessage(
            uri ?? throw new ArgumentNullException(nameof(uri)),
            requiredVersion ?? throw new ArgumentNullException(nameof(requiredVersion))))
    {
        Uri = uri;
        RequiredVersion = requiredVersion;
    }

    /// <summary>
    /// Gets the file that was refused.
    /// </summary>
    public Uri Uri { get; }

    /// <summary>
    /// Gets the minimum version of Beutl the file records, as written in it.
    /// </summary>
    public string RequiredVersion { get; }

    private static string CreateMessage(Uri uri, string requiredVersion)
    {
        string file = uri.IsFile ? uri.LocalPath : uri.ToString();
        // Anything else is refused because it is not a version string this build can compare.
        bool newer = NuGetVersion.TryParse(requiredVersion, out NuGetVersion? required)
                     && NuGetVersion.TryParse(BeutlApplication.Version, out NuGetVersion? running)
                     && VersionComparer.VersionRelease.Compare(required, running) > 0;
        return newer
            ? $"'{file}' requires Beutl {requiredVersion} or later; this is Beutl {BeutlApplication.Version}."
            : $"'{file}' records the minimum Beutl version '{requiredVersion}', which Beutl {BeutlApplication.Version} cannot read.";
    }
}
