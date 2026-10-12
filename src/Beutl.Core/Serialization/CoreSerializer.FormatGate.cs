using System.Text.Json;
using System.Text.Json.Nodes;
using NuGet.Versioning;

namespace Beutl.Serialization;

public static partial class CoreSerializer
{
    // Orders the reads and rewrites of project files, including those of a scene's elements saved in
    // parallel, so that every write finds the project file already raised.
    private static readonly object s_projectFileSync = new();

    /// <summary>
    /// The load gate, which every read of a file-backed object passes through before anything else reads
    /// the file's object. It takes the version fields out, so they never become members of the object, and
    /// refuses a file whose <c>minAppVersion</c> is above the running version.
    /// </summary>
    private static StoredVersion TakeVersionFields(JsonObject json, Uri uri)
    {
        string? appVersion = json.TryGetPropertyValue(FileFormat.AppVersionKey, out JsonNode? appNode)
                             && appNode is JsonValue appValue
                             && appValue.TryGetValue(out string? recordedAppVersion)
            ? recordedAppVersion
            : null;
        json.Remove(FileFormat.AppVersionKey);

        string? minAppVersion = null;
        if (json.TryGetPropertyValue(FileFormat.MinAppVersionKey, out JsonNode? minNode) && minNode is not null)
        {
            if (minNode is not JsonValue minValue || !minValue.TryGetValue(out minAppVersion))
            {
                throw new UnsupportedFileVersionException(uri, minNode.ToJsonString());
            }

            if (!NuGetVersion.TryParse(minAppVersion, out NuGetVersion? required)
                || VersionComparer.VersionRelease.Compare(required, NuGetVersion.Parse(BeutlApplication.Version)) > 0)
            {
                throw new UnsupportedFileVersionException(uri, minAppVersion);
            }
        }

        json.Remove(FileFormat.MinAppVersionKey);
        return new StoredVersion(appVersion, minAppVersion);
    }

    private static void RecordRead(ICoreSerializable obj, StoredVersion version, Uri uri)
    {
        RootFileVersions.RecordRead(obj, version.MinAppVersion);
        // Another file read with an appVersion, which earlier development builds wrote, loses it on its next save.
        if (obj is Project project)
        {
            RootFileVersions.RecordAppVersion(project, version.AppVersion);
            if (uri.IsFile && version.MinAppVersion is { } minAppVersion)
            {
                RememberProjectFile(project, uri.LocalPath, minAppVersion);
            }
        }
    }

    // Whether obj's file is a root file, which carries the version fields: the file of a project, a scene or an
    // element, the objects of the project hierarchy. Another file-backed object keeps the format it defines; a
    // scene's media fingerprint file, for example, states a format version of its own.
    private static bool IsRootFile(ICoreSerializable obj)
    {
        return obj is IHierarchical;
    }

    // What a save writes as minAppVersion into obj's root file: the higher of what obj's file recorded
    // when it was read, or what an earlier save of obj wrote, and this build's floor.
    private static string GetMinAppVersionToWrite(ICoreSerializable obj)
    {
        return FileFormat.Max(RootFileVersions.GetMinAppVersion(obj), FileFormat.Floor);
    }

    // The project obj's file belongs to: obj's nearest project ancestor, or, outside the hierarchy, the
    // project obj was read with.
    private static Project? FindProject(ICoreSerializable obj)
    {
        return obj is IHierarchical hierarchical && hierarchical.FindHierarchicalParent<Project>() is { } project
            ? project
            : RootFileVersions.GetProject(obj);
    }

    // Writes minAppVersion, and appVersion when one is given, which only a project file records. A field
    // the file already has stays where it is. A new one goes first, after the Id and Name an object such as
    // a project starts with, which is where project files have always had them, and minAppVersion right
    // after appVersion.
    private static void WriteVersionFields(JsonObject json, string? appVersion, string minAppVersion)
    {
        if (appVersion is not null)
        {
            SetVersionField(json, FileFormat.AppVersionKey, appVersion, CountLeadingIdAndName(json));
        }

        int appVersionIndex = json.IndexOf(FileFormat.AppVersionKey);
        SetVersionField(
            json,
            FileFormat.MinAppVersionKey,
            minAppVersion,
            appVersionIndex >= 0 ? appVersionIndex + 1 : CountLeadingIdAndName(json));
    }

    private static void SetVersionField(JsonObject json, string key, string value, int index)
    {
        if (json.ContainsKey(key))
        {
            json[key] = value;
        }
        else
        {
            json.Insert(index, key, value);
        }
    }

    private static int CountLeadingIdAndName(JsonObject json)
    {
        int count = 0;
        while (count < json.Count && json.GetAt(count).Key is nameof(CoreObject.Id) or nameof(CoreObject.Name))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Makes the file of <paramref name="project"/> record at least <paramref name="minAppVersion"/>
    /// before another file of the project, which this build writes, reaches the disk.
    /// </summary>
    /// <remarks>
    /// Only the two version fields are rewritten: the save that follows can still fail, and the project file
    /// must not then point at files that were never written. A project without a file yet is left alone,
    /// because the save that writes it records the version; so is a file no build can open. A file that cannot
    /// be read stops the write, because a save must not walk past a gate it could not establish.
    /// </remarks>
    private static void RaiseProjectFile(Project project, string minAppVersion)
    {
        // Recorded even when nothing is on disk yet, so the save that writes the project file later does
        // not record less than the files written before it.
        string required = RootFileVersions.RaiseMinAppVersion(project, minAppVersion);
        if (project.Uri is not { IsFile: true } projectUri)
        {
            return;
        }

        string path = projectUri.LocalPath;
        lock (s_projectFileSync)
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return;
            }

            // A save writes many files, and the project file it raised or read for the first is still the
            // same file for the others. One changed outside Beutl, by a checkout for example, has another
            // timestamp or length, and is read again.
            if (RootFileVersions.GetProjectFileState(project) is { } known
                && string.Equals(known.Path, path, StringComparison.Ordinal)
                && known.LastWriteTimeUtc == info.LastWriteTimeUtc
                && known.Length == info.Length
                && FileFormat.IsAtLeast(known.MinAppVersion, required))
            {
                return;
            }

            JsonNode? node;
            try
            {
                using FileStream stream = File.OpenRead(path);
                node = JsonNode.Parse(stream);
            }
            catch (JsonException)
            {
                return;
            }

            if (node is not JsonObject json)
            {
                return;
            }

            if (!RecordsLowerVersion(json, required))
            {
                if (json[FileFormat.MinAppVersionKey] is JsonValue value && value.TryGetValue(out string? recorded))
                {
                    RememberProjectFile(project, path, recorded);
                }

                return;
            }

            WriteVersionFields(json, BeutlApplication.Version, required);
            WriteJsonAtomically(
                path,
                isCompatibilityGate: true,
                writer => json.WriteTo(writer, JsonHelper.SerializerOptions));
            RootFileVersions.RecordAppVersion(project, BeutlApplication.Version);
            RememberProjectFile(project, path, required);
        }
    }

    // Remembers what the project file at path records, with its timestamp and length now, so that a later
    // write that needs no more skips reading it while nothing else changes it. A storage write transaction
    // that rolls back puts other bytes there, so it forgets them.
    private static void RememberProjectFile(Project project, string path, string minAppVersion)
    {
        var info = new FileInfo(path);
        if (!info.Exists || !NuGetVersion.TryParse(minAppVersion, out _))
        {
            return;
        }

        RootFileVersions.SetProjectFileState(
            project,
            new RootFileVersions.ProjectFileState(path, info.LastWriteTimeUtc, info.Length, minAppVersion));
        StorageWriteTransaction.Current?.OnRollback(() => RootFileVersions.SetProjectFileState(project, null));
    }

    // Whether a project file records a minAppVersion below required. A version this build cannot compare,
    // or one that is not a string, is kept: no released build opens such a file either.
    private static bool RecordsLowerVersion(JsonObject json, string required)
    {
        return json[FileFormat.MinAppVersionKey] switch
        {
            null => true,
            JsonValue value when value.TryGetValue(out string? recorded) =>
                NuGetVersion.TryParse(recorded, out NuGetVersion? recordedVersion)
                && NuGetVersion.TryParse(required, out NuGetVersion? requiredVersion)
                && VersionComparer.VersionRelease.Compare(recordedVersion, requiredVersion) < 0,
            _ => false,
        };
    }

    private readonly record struct StoredVersion(string? AppVersion, string? MinAppVersion);
}
