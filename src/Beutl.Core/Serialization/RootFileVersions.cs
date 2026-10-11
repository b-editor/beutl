using System.Runtime.CompilerServices;

namespace Beutl.Serialization;

/// <summary>
/// Remembers, for each object read from or written to a root file, the version fields of that file and
/// the project the object was read with.
/// </summary>
/// <remarks>
/// <para>
/// The fields travel with the object rather than with a path, so a Save As writes what the content
/// requires wherever it goes. Entries are keyed weakly on the object and live exactly as long as it does.
/// </para>
/// <para>
/// The project is what lets a file-backed object outside the hierarchy, saved on its own, find the
/// project file to raise first. Objects in the hierarchy find it through their ancestors.
/// </para>
/// </remarks>
internal static class RootFileVersions
{
    private static readonly ConditionalWeakTable<ICoreSerializable, Entry> s_entries = new();

    // The project whose file is being read on this flow, so the files it references can record it.
    // Async-local because a scene reads its element files in parallel.
    private static readonly AsyncLocal<Project?> s_readingProject = new();

    internal static string? GetAppVersion(ICoreSerializable obj)
    {
        return s_entries.TryGetValue(obj, out Entry? entry) ? entry.AppVersion : null;
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
    /// Records the version fields <paramref name="obj"/> was read with, and the project being read
    /// around it, if any.
    /// </summary>
    internal static void RecordRead(ICoreSerializable obj, string? appVersion, string? minAppVersion)
    {
        Entry entry = s_entries.GetValue(obj, static _ => new Entry());
        lock (entry)
        {
            entry.AppVersion = appVersion;
            entry.MinAppVersion = minAppVersion;
            entry.Project = obj is Project ? null : s_readingProject.Value;
        }
    }

    /// <summary>
    /// Records the version fields a save wrote for <paramref name="obj"/>.
    /// </summary>
    internal static void RecordWrite(ICoreSerializable obj, string appVersion, string minAppVersion)
    {
        Entry entry = s_entries.GetValue(obj, static _ => new Entry());
        lock (entry)
        {
            entry.AppVersion = appVersion;
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

    internal static void RecordAppVersion(Project project, string appVersion)
    {
        Entry entry = s_entries.GetValue(project, static _ => new Entry());
        lock (entry)
        {
            entry.AppVersion = appVersion;
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

    private sealed class Entry
    {
        public string? AppVersion;

        public string? MinAppVersion;

        public Project? Project;
    }
}
