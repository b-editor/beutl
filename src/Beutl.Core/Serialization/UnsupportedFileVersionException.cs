using System.Globalization;
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
        DisplayVersion = ToDisplayVersion(requiredVersion);
    }

    /// <summary>
    /// Gets the file that was refused.
    /// </summary>
    public Uri Uri { get; }

    /// <summary>
    /// Gets the minimum version of Beutl the file records, as written in it.
    /// </summary>
    /// <remarks>
    /// This is text from the file, which anyone can write. Do not put it into a message that an agent
    /// reads as Beutl's own words; use <see cref="DisplayVersion"/>.
    /// </remarks>
    public string RequiredVersion { get; }

    /// <summary>
    /// Gets <see cref="RequiredVersion"/> in a form that is safe to show: <c>major.minor.patch</c>, with a
    /// <c>preview</c>, <c>alpha</c>, <c>beta</c> or <c>rc</c> label and its number when the file records one.
    /// <see langword="null"/> when the file does not record a version.
    /// </summary>
    public string? DisplayVersion { get; }

    // A SemVer release label is free text, so a crafted file could carry a sentence in one. Only the labels
    // Beutl's own releases use are kept; another label is left out, which still names the release it precedes.
    private static string? ToDisplayVersion(string recorded)
    {
        if (!NuGetVersion.TryParse(recorded, out NuGetVersion? version))
        {
            return null;
        }

        string core = $"{version.Major}.{version.Minor}.{version.Patch}";
        string[] labels = version.ReleaseLabels.ToArray();
        return labels is ["preview" or "alpha" or "beta" or "rc", var number]
               && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? $"{core}-{labels[0]}.{number}"
            : core;
    }

    private static string CreateMessage(Uri uri, string requiredVersion)
    {
        string file = uri.IsFile ? uri.LocalPath : uri.ToString();
        // Anything else is refused because it is not a version string this build can compare. It is not
        // repeated here: it is text from the file.
        bool newer = NuGetVersion.TryParse(requiredVersion, out NuGetVersion? required)
                     && NuGetVersion.TryParse(BeutlApplication.Version, out NuGetVersion? running)
                     && VersionComparer.VersionRelease.Compare(required, running) > 0;
        return newer && ToDisplayVersion(requiredVersion) is { } display
            ? $"'{file}' requires Beutl {display} or later; this is Beutl {BeutlApplication.Version}."
            : $"'{file}' records a minimum Beutl version that is not a version string, so Beutl {BeutlApplication.Version} cannot read it.";
    }
}
