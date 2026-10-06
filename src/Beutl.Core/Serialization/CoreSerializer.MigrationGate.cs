using System.Text.Json;
using System.Text.Json.Nodes;
using NuGet.Versioning;

namespace Beutl.Serialization;

public static partial class CoreSerializer
{
    // Complete this preflight for the whole save before replacing any migrated sidecar.
    internal static void PersistProjectMigrationMetadata(IEnumerable<CoreObject> objects)
    {
        CoreObject[] pending = objects as CoreObject[] ?? objects.ToArray();

        var projects = new HashSet<Project>();
        foreach (CoreObject obj in pending)
        {
            if (obj is Project project)
                projects.Add(project);
            if (obj is IHierarchical hierarchical)
            {
                foreach (Project ancestor in hierarchical.EnumerateAncestors<Project>())
                    projects.Add(ancestor);
            }
        }

        RaiseAttachedMigrationsIfUncovered(
            pending,
            projects.Select(project => (project, project.Uri)).ToArray());

        foreach (Project project in projects)
        {
            if (project.Uri is not null)
            {
                WriteMigrationGate(project, project.Uri);
            }
        }
    }

    /// <summary>
    /// The same preflight for a project being written to a named destination, which is not always
    /// the URI it currently carries: a first save has none, and a Save As names a different one.
    /// </summary>
    private static void PersistProjectMigrationGate(Project project, Uri destination)
    {
        RaiseAttachedMigrationsIfUncovered([project], [(project, destination)]);
        WriteMigrationGate(project, destination);
    }

    /// <summary>
    /// Discovers only while a guarded project's gate does not already cover every requirement a live
    /// value still carries.
    /// </summary>
    /// <remarks>
    /// That is what a value assigned to a new owner looks like from here, whether it is reaching its
    /// first owner or a second one, and it is equally why a graph that has taken its requirements
    /// over never pays for the pass again.
    /// </remarks>
    private static void RaiseAttachedMigrationsIfUncovered(
        CoreObject[] pending,
        IReadOnlyCollection<(Project Project, Uri? Destination)> guarded)
    {
        if (AttachedContentMigrations.HighestRetained is { } outstanding
            && guarded.Any(entry => !CoversMigration(entry.Project, entry.Destination, outstanding)))
        {
            RaiseAttachedMigrations(pending);
        }
    }

    private static void WriteMigrationGate(Project project, Uri destination)
    {
        string? migrated = null;
        foreach (ProjectItem item in project.Items)
        {
            migrated = Project.GetMaximumMigrationVersion(
                migrated,
                Project.GetRequiredMigrationVersion(item));
        }

        // Only a migration this session advances the recorded application version; a plain load and
        // save keeps the one from disk.
        if (migrated is not null)
        {
            project.MarkAsMigrated(migrated);
        }
        else if (ReadPersistedGate(destination) is not { } persisted
                 || IsCoveredBy(project.MinAppVersion, persisted))
        {
            // Nothing migrated here, and the destination either advertises at least what this
            // project constrains or holds nothing an older application could open. A project
            // reloaded at a raised version reports no migration of its own but still writes content
            // that constraint guards, which is why the destination is compared at all. A gate that
            // cannot be read counts as strict enough, the way Project.MarkAsMigrated retains an
            // unknown persisted constraint rather than weakening it.
            return;
        }

        if (destination.Scheme != "file" || !TryWriteMigrationGate(project, destination.LocalPath))
        {
            StoreToUri(project, destination, CoreSerializationMode.Write);
        }
    }

    private static bool IsCoveredBy(string version, string gate)
    {
        return ReferenceEquals(Project.GetMaximumVersion(gate, version), gate);
    }

    /// <summary>
    /// Puts the project's version metadata at <paramref name="path"/> without recording the item
    /// graph, and reports whether it could.
    /// </summary>
    /// <remarks>
    /// This preflight runs before the save it guards, and that save can still fail — after which
    /// <c>ProjectPersistence</c> rolls the in-memory item list back. Re-serializing the graph here
    /// would leave the file recording a graph that never happened and pointing at sidecars that were
    /// never written, so a file that is already there keeps the graph it records and a destination
    /// with none gets the gate alone, which points at nothing. A location that cannot hold the gate
    /// is not skipped: its failure stops the save before any sidecar is replaced.
    /// </remarks>
    private static bool TryWriteMigrationGate(Project project, string path)
    {
        JsonObject json;
        if (File.Exists(path))
        {
            JsonNode? node;
            try
            {
                using FileStream stream = File.OpenRead(path);
                node = JsonNode.Parse(stream);
            }
            catch (JsonException)
            {
                // Unreadable bytes are not a graph worth preserving, and refusing here would leave
                // a malformed destination unsaveable. Only a failure to reach the file at all is
                // allowed to stop the save.
                return false;
            }

            // Nothing to raise in place; the caller writes the file the ordinary way.
            if (node is not JsonObject existing)
            {
                return false;
            }

            json = existing;
        }
        else
        {
            json = [];
            json.WriteDiscriminator(typeof(Project));
        }

        // A gate already on disk is never lowered, the way Project.MarkAsMigrated treats the one it
        // loaded: a Save As can name a file whose own constraint is higher than this project's.
        string persisted;
        switch (json["minAppVersion"])
        {
            case null:
                persisted = Project.DefaultMinAppVersion;
                break;
            case JsonValue value when value.TryGetValue(out string? persistedGate):
                persisted = persistedGate;
                break;
            default:
                // A gate that is not a string is corruption no load could read either; leave the
                // ordinary write to replace the file rather than patching around it.
                return false;
        }

        json["minAppVersion"] = Project.GetMaximumVersion(persisted, project.MinAppVersion);
        json["appVersion"] = project.AppVersion;

        string? directory = Path.GetDirectoryName(path);
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = StorageWriteTransaction.CreateTemporaryFile(temporaryPath, path))
            using (var writer = new Utf8JsonWriter(stream, JsonHelper.WriterOptions))
            {
                json.WriteTo(writer, JsonHelper.SerializerOptions);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            StorageWriteTransaction.MoveIntoPlace(
                temporaryPath,
                path,
                overwrite: true,
                isCompatibilityGate: true);
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch
            {
                // 失敗しても元の例外は投げる
            }

            throw;
        }

        return true;
    }

    /// <summary>
    /// Whether the gate this save is about to make durable already covers
    /// <paramref name="requiredVersion"/>, so that discovering it could not change what reaches the
    /// disk ahead of the sidecars.
    /// </summary>
    /// <remarks>
    /// The gate that counts is the one in the file being written, which a Save As can name somewhere
    /// the project has never been, and which is what an older application reads if the save fails
    /// part-way — not the constraint the project happens to hold in memory. An item that already
    /// carries the requirement counts too, because the write below then takes it from there.
    /// Unparseable versions answer "covered" rather than throwing: an unknown persisted constraint is
    /// retained rather than weakened (see Project.MarkAsMigrated), so nothing this pass could
    /// discover would change the gate, and a save must not fail over a value it cannot read.
    /// </remarks>
    private static bool CoversMigration(Project project, Uri? destination, string requiredVersion)
    {
        if (!NuGetVersion.TryParse(requiredVersion, out NuGetVersion? required))
        {
            return true;
        }

        if (destination is not null && ReadGateForComparison(destination) is { } persisted)
        {
            if (!NuGetVersion.TryParse(persisted, out NuGetVersion? gate)
                || VersionComparer.VersionRelease.Compare(gate, required) >= 0)
            {
                return true;
            }
        }

        foreach (ProjectItem item in project.Items)
        {
            if (Project.GetRequiredMigrationVersion(item) is { } known
                && NuGetVersion.TryParse(known, out NuGetVersion? knownVersion)
                && VersionComparer.VersionRelease.Compare(knownVersion, required) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Raises the migration requirement of every value that reports one only while its owner is
    /// written, so the preflight above still runs before the files carrying those values.
    /// </summary>
    /// <remarks>
    /// A value deserialized on its own hands its requirement to its owner during serialization, and
    /// waiting for the real write would put the compatibility gate behind the sidecars it guards.
    /// Serializing to a discarded buffer first is the same discovery <c>SceneRecovery</c> performs,
    /// and nothing reaches the disk: <see cref="CoreSerializationMode.Write"/> on its own leaves a
    /// referenced object as a URI, and the caller runs this only while a gate about to be written
    /// does not already cover every requirement a live value carries.
    /// </remarks>
    /// <summary>
    /// The minimum application version the file at <paramref name="destination"/> advertises, or
    /// <see langword="null"/> when it advertises none this can compare against.
    /// </summary>
    /// <remarks>
    /// Nothing there, and bytes that are not a project object at all, answer <see langword="null"/>:
    /// neither is something an older application opens. A file that exists but cannot be read is not
    /// that case and throws, because a save must not walk past a gate it could not establish. Only
    /// reading is attempted; a file that cannot be written is caught by the write itself.
    /// </remarks>
    private static string? ReadPersistedGate(Uri destination)
    {
        if (destination.Scheme != "file")
        {
            return null;
        }

        string path = destination.LocalPath;
        if (!File.Exists(path))
        {
            return null;
        }

        JsonNode? node;
        try
        {
            using FileStream stream = File.OpenRead(path);
            node = JsonNode.Parse(stream);
        }
        catch (JsonException)
        {
            return null;
        }

        if (node is not JsonObject json)
        {
            return null;
        }

        // A project that records no gate still opens, at the oldest minimum Project.Deserialize
        // assumes for it, so it is a gate to compare against rather than nothing.
        return json["minAppVersion"] is JsonValue value && value.TryGetValue(out string? persisted)
            ? persisted
            : Project.DefaultMinAppVersion;
    }

    // Deciding whether to do more work must not be what fails a save: an unreadable destination
    // answers "not covered" here, and WriteMigrationGate is where that same read reports it.
    private static string? ReadGateForComparison(Uri destination)
    {
        try
        {
            return ReadPersistedGate(destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void RaiseAttachedMigrations(CoreObject[] objects)
    {
        var visited = new HashSet<CoreObject>(ReferenceEqualityComparer.Instance);
        var embedded = new HashSet<CoreObject>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(CoreObject Object, bool OwnsFile)>();
        foreach (CoreObject obj in objects)
        {
            pending.Push((obj, true));
        }

        while (pending.TryPop(out (CoreObject Object, bool OwnsFile) current))
        {
            if (!visited.Add(current.Object))
            {
                continue;
            }

            // Only a root of this save and a descendant with a file of its own are written on their
            // own; everything else is embedded in the nearest such ancestor and covered by its pass.
            // A file-backed object reached through an ordinary property is embedded too, so a value it
            // holds reports before the gate is decided rather than when the real save writes its file.
            // A descendant an earlier pass already embedded has reported through it.
            if (current.OwnsFile && !embedded.Contains(current.Object))
            {
                using var capture = new SerializedObjectCapture();
                SerializeToJsonObject(
                    current.Object,
                    new CoreSerializerOptions
                    {
                        BaseUri = current.Object.Uri,
                        Mode = CoreSerializationMode.Write | CoreSerializationMode.EmbedReferencedObjects,
                    });
                embedded.UnionWith(capture.Objects);
            }

            if (current.Object is IHierarchical hierarchical)
            {
                foreach (IHierarchical child in hierarchical.HierarchicalChildren)
                {
                    if (child is CoreObject coreObject)
                    {
                        pending.Push((coreObject, coreObject.Uri is not null));
                    }
                }
            }
        }
    }
}
