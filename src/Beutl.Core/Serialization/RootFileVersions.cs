using System.Runtime.CompilerServices;

namespace Beutl.Serialization;

/// <summary>
/// Remembers, for each object read from or written to a root file, the <c>minAppVersion</c> of that file
/// and the project the object was read with, and for a project also the <c>appVersion</c> of its file.
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

    // Only a project file records the version that last saved it.
    internal static string? GetAppVersion(Project project)
    {
        return s_entries.TryGetValue(project, out Entry? entry) ? entry.AppVersion : null;
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
    /// Records the <c>minAppVersion</c> a save wrote for <paramref name="obj"/>.
    /// </summary>
    internal static void RecordWrite(ICoreSerializable obj, string minAppVersion)
    {
        Entry entry = s_entries.GetValue(obj, static _ => new Entry());
        lock (entry)
        {
            entry.MinAppVersion = FileFormat.Max(entry.MinAppVersion, minAppVersion);
        }
    }

    /// <summary>
    /// Raises the <c>minAppVersion</c> recorded for <paramref name="project"/> to at least
    /// <paramref name="minAppVersion"/> and returns the result.
    /// </summary>
    internal static string RaiseMinAppVersion(Project project, string minAppVersion)
    {
        Entry entry = s_entries.GetValue(project, static _ => new Entry());
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
    /// returned scope is disposed.
    /// </summary>
    internal static ReadingScope BeginReading(Project project)
    {
        Project? previous = s_readingProject.Value;
        s_readingProject.Value = project;
        return new ReadingScope(previous);
    }

    internal readonly struct ReadingScope(Project? previous) : IDisposable
    {
        public void Dispose() => s_readingProject.Value = previous;
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
    }
}
