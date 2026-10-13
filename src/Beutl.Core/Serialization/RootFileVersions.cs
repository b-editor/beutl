using System.Runtime.CompilerServices;
using NuGet.Versioning;

namespace Beutl.Serialization;

/// <summary>
/// Remembers, for each object read from or written to a root file, the <c>minAppVersion</c> of that file
/// and the project the object was read with, and for a project also the <c>appVersion</c> of its file and
/// whether its last read kept data this build does not know.
/// </summary>
/// <remarks>
/// <para>
/// The fields travel with the object rather than with a path, so a Save As writes what the content
/// requires wherever it goes. Entries are keyed weakly on the object and live exactly as long as it does.
/// </para>
/// <para>
/// The project is what lets a file-backed object outside the hierarchy, saved on its own, find the
/// project file to raise first. Objects in the hierarchy find it through their ancestors. A file read
/// with a project also raises the project's own requirement to what the file records, so that any later
/// save of the project file records at least that, even one that writes no other file.
/// </para>
/// </remarks>
internal static class RootFileVersions
{
    private static readonly ConditionalWeakTable<ICoreSerializable, Entry> s_entries = new();

    // The project whose file is being read on this flow, so the files it references can record it.
    // Async-local because a scene reads its element files in parallel.
    private static readonly AsyncLocal<Project?> s_readingProject = new();

    internal static string? GetAppVersion(Project project)
    {
        return s_entries.TryGetValue(project, out Entry? entry) ? entry.AppVersion : null;
    }

    /// <summary>
    /// Returns the version that last saved <paramref name="project"/>, as its file records it, in normalized form,
    /// or <see langword="null"/> when the file records none or the value is not a version.
    /// </summary>
    /// <remarks>
    /// The value comes from the file, so a message that names it uses this instead of the recorded text: anything
    /// that is not a version never reaches the user or an agent.
    /// </remarks>
    internal static string? GetLastSavedVersion(Project project)
    {
        return GetAppVersion(project) is { } recorded && NuGetVersion.TryParse(recorded, out NuGetVersion? version)
            ? version.ToNormalizedString()
            : null;
    }

    internal static string? GetMinAppVersion(ICoreSerializable obj)
    {
        return s_entries.TryGetValue(obj, out Entry? entry) ? entry.MinAppVersion : null;
    }

    internal static Project? GetProject(ICoreSerializable obj)
    {
        return s_entries.TryGetValue(obj, out Entry? entry) ? entry.Project : null;
    }

    /// <summary>
    /// Returns whether the last read of <paramref name="project"/> kept data this build does not know: members
    /// a type does not read, or objects whose type could not be loaded, which keep their JSON.
    /// </summary>
    internal static bool HasKeptUnknownData(Project project)
    {
        if (!s_entries.TryGetValue(project, out Entry? entry))
        {
            return false;
        }

        lock (entry)
        {
            return entry.KeptUnknownData;
        }
    }

    /// <summary>
    /// Records that the read of the project on this flow, if any, kept data this build does not know.
    /// </summary>
    internal static void RecordKeptUnknownData()
    {
        if (s_readingProject.Value is not { } project)
        {
            return;
        }

        Entry entry = s_entries.GetValue(project, static _ => new Entry());
        lock (entry)
        {
            entry.KeptUnknownData = true;
        }
    }

    /// <summary>
    /// Records the <c>minAppVersion</c> <paramref name="obj"/>'s file recorded when it was read, and the
    /// project being read around it, if any, whose requirement it raises.
    /// </summary>
    internal static void RecordRead(ICoreSerializable obj, string? minAppVersion)
    {
        Project? project = obj is Project ? null : s_readingProject.Value;
        Entry entry = s_entries.GetValue(obj, static _ => new Entry());
        lock (entry)
        {
            entry.MinAppVersion = minAppVersion;
            entry.Project = project;
        }

        if (project is not null && minAppVersion is not null)
        {
            RaiseMinAppVersion(project, minAppVersion);
        }
    }

    /// <summary>
    /// Raises what <paramref name="obj"/> records to at least <paramref name="minAppVersion"/> and returns it.
    /// </summary>
    internal static string RaiseMinAppVersion(ICoreSerializable obj, string minAppVersion)
    {
        Entry entry = s_entries.GetValue(obj, static _ => new Entry());
        lock (entry)
        {
            entry.MinAppVersion = FileFormat.Max(entry.MinAppVersion, minAppVersion);
            return entry.MinAppVersion;
        }
    }

    /// <summary>
    /// Records the <c>appVersion</c> the file of <paramref name="project"/> holds, as read or as written.
    /// </summary>
    internal static void RecordAppVersion(Project project, string? appVersion)
    {
        Entry entry = s_entries.GetValue(project, static _ => new Entry());
        lock (entry)
        {
            entry.AppVersion = appVersion;
        }
    }

    internal static ProjectFileState? GetProjectFileState(Project project)
    {
        if (!s_entries.TryGetValue(project, out Entry? entry))
        {
            return null;
        }

        lock (entry)
        {
            return entry.ProjectFile;
        }
    }

    internal static void SetProjectFileState(Project project, ProjectFileState? state)
    {
        Entry entry = s_entries.GetValue(project, static _ => new Entry());
        lock (entry)
        {
            entry.ProjectFile = state;
        }
    }

    /// <summary>
    /// Makes <paramref name="project"/> the project that files read on this flow belong to, until the
    /// returned scope is disposed. Without a project, the files keep the project being read around them.
    /// </summary>
    internal static ReadingScope BeginReading(Project? project)
    {
        if (project is null)
        {
            return default;
        }

        Entry entry = s_entries.GetValue(project, static _ => new Entry());
        lock (entry)
        {
            entry.KeptUnknownData = false;
        }

        Project? previous = s_readingProject.Value;
        s_readingProject.Value = project;
        return new ReadingScope(previous, restore: true);
    }

    internal readonly struct ReadingScope(Project? previous, bool restore) : IDisposable
    {
        public void Dispose()
        {
            if (restore)
            {
                s_readingProject.Value = previous;
            }
        }
    }

    /// <summary>
    /// The <c>minAppVersion</c> a project file on disk is known to record, as this process last read or
    /// wrote it, with the timestamp and length the file had then.
    /// </summary>
    internal readonly record struct ProjectFileState(
        string Path,
        DateTime LastWriteTimeUtc,
        long Length,
        string MinAppVersion);

    private sealed class Entry
    {
        public string? MinAppVersion;

        public Project? Project;

        // Kept for a project only.
        public string? AppVersion;

        public ProjectFileState? ProjectFile;

        public bool KeptUnknownData;
    }
}
