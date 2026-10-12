using NuGet.Versioning;

namespace Beutl.Serialization;

/// <summary>
/// The version fields Beutl writes into root files and checks when it reads one.
/// </summary>
/// <remarks>
/// <para>
/// A root file is the file of an object in the project hierarchy: a project (<c>.bep</c>), a scene
/// (<c>.scene</c>) or an element (<c>.belm</c>). Every save writes <c>minAppVersion</c>, the oldest
/// version that can read the file, into each root file it writes: the higher of what the file recorded
/// when it was read and <see cref="Floor"/>, and in a project file at least what the other files of the
/// project require. A project file also records <c>appVersion</c>, the version of Beutl that saved it
/// last. Scene and element files do not, so that a newer version that saves them unchanged leaves them
/// byte for byte as they were. The fields belong to the file, not to the object: they are not members,
/// so an embedded or copied object carries neither.
/// </para>
/// <para>
/// One gate reads every file the serializer reads an object from, and refuses one whose
/// <c>minAppVersion</c> is above the running version with an <see cref="UnsupportedFileVersionException"/>.
/// Released builds check only the project file, so the first write to a project after <see cref="Floor"/>
/// was raised, including a write of a file-backed object outside the hierarchy, first rewrites the project
/// file's two version fields, and nothing else in it.
/// </para>
/// </remarks>
public static class FileFormat
{
    /// <summary>
    /// The oldest Beutl version that can read what this build writes.
    /// </summary>
    /// <remarks>
    /// Raise it only for a breaking change, in the pull request that adds that change's migration. A
    /// breaking change changes the meaning of an existing member, changes the structure of persisted
    /// data, or writes a type or member under a new name. New members and new types do not raise it.
    /// </remarks>
    public const string Floor = "2.0.0-preview.1";

    internal const string AppVersionKey = "appVersion";

    internal const string MinAppVersionKey = "minAppVersion";

    /// <summary>
    /// Returns whether <paramref name="recorded"/> is a version this build can compare and is at least
    /// <paramref name="required"/>.
    /// </summary>
    internal static bool IsAtLeast(string recorded, string required)
    {
        return NuGetVersion.TryParse(recorded, out NuGetVersion? recordedVersion)
               && NuGetVersion.TryParse(required, out NuGetVersion? requiredVersion)
               && VersionComparer.VersionRelease.Compare(recordedVersion, requiredVersion) >= 0;
    }

    /// <summary>
    /// Returns the higher of <paramref name="recorded"/> and <paramref name="required"/>, or
    /// <paramref name="recorded"/> when it is not a version this build can compare, so a constraint it
    /// cannot read is kept rather than weakened.
    /// </summary>
    internal static string Max(string? recorded, string required)
    {
        if (recorded is null)
        {
            return required;
        }

        return NuGetVersion.TryParse(recorded, out NuGetVersion? recordedVersion)
               && NuGetVersion.TryParse(required, out NuGetVersion? requiredVersion)
               && VersionComparer.VersionRelease.Compare(recordedVersion, requiredVersion) < 0
            ? required
            : recorded;
    }
}
