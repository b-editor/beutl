using System.Text.Json;
using Beutl.Editor.Services;
using Beutl.Graphics3D.Models;
using Beutl.Media.Decoding;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor;

internal sealed partial class ProjectAssetService(DecoderRegistry decoders)
{
    private static readonly string s_modelType = TypeFormat.ToString(typeof(Model3D));
    private static readonly HashSet<string> s_modelExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".obj", ".fbx", ".gltf", ".glb", ".dae", ".stl", ".ply", ".blend", ".3ds",
    };

    internal sealed record Relink(ProjectAssetReferences.Use Use, string Destination);
    internal sealed record Analysis(ProjectAssetPreview Preview, Dictionary<string, List<Relink>> Sources);

    internal Analysis Analyze(ProjectAssetPaths paths, ProjectAssetReferences references, Guid batch, CancellationToken token)
    {
        FilePathComparison.ResolutionContext canonical = FilePathComparison.CreateResolutionContext();
        var used = references.InUse.Select(canonical.ResolveCanonicalPath).ToHashSet(StringComparer.Ordinal);
        var retained = references.RetainedByHistory.Select(canonical.ResolveCanonicalPath).ToHashSet(StringComparer.Ordinal);
        var sourceGroups = new Dictionary<string, List<Relink>>(StringComparer.Ordinal);
        var bundledDependencies = new Dictionary<string, (string Model, string Destination)>(StringComparer.Ordinal);
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var external = new List<ProjectAssetItem>();
        bool shared = false;
        bool incomplete = references.Incomplete;
        try
        {
            string[] savedDocuments = [.. ProjectAssetPaths.EnumerateFiles(paths.Root, token).Where(IsProjectDocument)];
            shared = savedDocuments.Any(file => HasExtension(file, EditorConstants.ProjectFileExtension)
                && !string.Equals(canonical.ResolveCanonicalPath(file), paths.ProjectFile, StringComparison.Ordinal));
            ReadSavedReferences(savedDocuments.Concat(references.Documents).Where(IsProjectDocument), used, canonical, paths, token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            incomplete = true;
            s_logger.LogWarning(ex, "Could not check every saved reference before organizing project assets.");
        }

        foreach (var group in references.Sources.Values.GroupBy(use => canonical.ResolveCanonicalPath(use.Uri.LocalPath))
                     .OrderByDescending(group => group.Any(use => use.Source is ModelSource))
                     .ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            var uses = group.ToArray();
            if (uses.All(use => use.Files.All(file => paths.IsInside(file) && paths.IsInside(canonical.ResolveCanonicalPath(file)))))
                continue;

            bool isModel = uses.Any(use => use.Source is ModelSource);
            string? existingDestination = !isModel && bundledDependencies.TryGetValue(group.Key, out var bundled)
                ? bundled.Destination : null;
            ProjectAssetItem item = PlanCollection(paths, uses, references, destinations, shared, existingDestination);
            if (existingDestination != null && item.CanApply(ProjectAssetAction.Collect))
            {
                // A model can expose its texture as a saved image source too. Keep the
                // image's references together with the copy of the model that supplies it.
                sourceGroups[bundledDependencies[group.Key].Model].AddRange(uses.Select(use => new Relink(use, existingDestination)));
                continue;
            }
            external.Add(item);
            sourceGroups.Add(item.Path, [.. uses.Select(use => new Relink(use, item.Destination))]);
            if (isModel && item.CanApply(ProjectAssetAction.Collect))
            {
                foreach (ProjectAssetFile file in item.Files.Skip(1))
                    bundledDependencies.TryAdd(canonical.ResolveCanonicalPath(file.Path), (item.Path, file.Destination));
            }
        }

        ProjectAssetIssue? cleanupIssue = shared ? ProjectAssetIssue.SharedReference
            : incomplete ? ProjectAssetIssue.IncompleteReferences
            : !paths.IsWritable(paths.Resources) || !paths.IsWritable(paths.Trash) ? ProjectAssetIssue.UnsafePath
            : null;
        var unused = new List<ProjectAssetItem>();
        if (paths.IsWritable(paths.Resources))
        {
            var extensions = MediaFileMatcher.ImagePatterns.Concat(MediaFileMatcher.VideoExtensions(decoders))
                .Concat(MediaFileMatcher.AudioExtensions(decoders))
                .Select(pattern => Path.GetExtension(pattern))
                .Concat(s_modelExtensions).Concat([".cube", ".mtl"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string fonts = Path.Combine(paths.Resources, "fonts");
            foreach (string path in ProjectAssetPaths.EnumerateFiles(paths.Resources, token))
            {
                if (!extensions.Contains(Path.GetExtension(path)) || ProjectAssetPaths.IsBelow(fonts, path)) continue;
                string identity = canonical.ResolveCanonicalPath(path);
                if (used.Contains(identity)) continue;
                string destination = Path.Combine(paths.Trash, batch.ToString("N"), Path.GetRelativePath(paths.Resources, path));
                unused.Add(PlanMove(path, destination, cleanupIssue ?? (retained.Contains(identity) ? ProjectAssetIssue.EditHistory : null)));
            }
        }

        var quarantined = new List<ProjectAssetItem>();
        if (paths.IsWritable(paths.Trash) && Directory.Exists(paths.Trash))
        {
            foreach (string folder in Directory.EnumerateDirectories(paths.Trash))
            {
                if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out _) || !paths.IsWritable(folder)) continue;
                foreach (string path in ProjectAssetPaths.EnumerateFiles(folder, token))
                {
                    string destination = Path.Combine(paths.Resources, Path.GetRelativePath(folder, path));
                    string identity = canonical.ResolveCanonicalPath(path);
                    ProjectAssetIssue? issue = incomplete || shared ? cleanupIssue
                        : used.Contains(identity) ? ProjectAssetIssue.InUse
                        : retained.Contains(identity) ? ProjectAssetIssue.EditHistory
                        : null;
                    ProjectAssetIssue? restoreIssue = issue != null ? null
                        : !paths.IsWritable(destination) ? ProjectAssetIssue.UnsafePath
                        : ProjectAssetPaths.GetAttributes(destination) != null ? ProjectAssetIssue.DestinationExists : null;
                    quarantined.Add(PlanMove(path, destination, issue) with { RestoreIssue = restoreIssue });
                }
            }
        }

        return new(new ProjectAssetPreview(batch, Sort(external), Sort(unused), Sort(quarantined), cleanupIssue), sourceGroups);
    }

    private static ProjectAssetItem[] Sort(List<ProjectAssetItem> items) =>
        [.. items.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Path, StringComparer.Ordinal)];

    private static ProjectAssetItem PlanCollection(ProjectAssetPaths paths, ProjectAssetReferences.Use[] uses,
        ProjectAssetReferences references, HashSet<string> destinations, bool shared, string? existingDestination)
    {
        var first = uses.FirstOrDefault(use => use.Source is ModelSource) ?? uses[0];
        string mainPath = Path.GetFullPath(first.Uri.LocalPath);
        bool isModel = first.Source is ModelSource;
        string requestedName = isModel ? Path.GetFileNameWithoutExtension(mainPath) : Path.GetFileName(mainPath);
        string destination = Path.Combine(paths.Resources, requestedName);
        try
        {
            ProjectAssetIssue? issue = shared || uses.Any(use => references.OtherEditorSources.Contains(use.Source)
                || use.Documents.Any(doc => doc.Uri is not { IsFile: true } uri || !paths.IsWritable(uri.LocalPath)))
                ? ProjectAssetIssue.SharedReference : null;
            if (!paths.IsWritable(paths.Resources)) issue ??= ProjectAssetIssue.UnsafePath;
            if (issue == null) destination = existingDestination ?? FindDestination(paths.Resources, requestedName, destinations);
            // A model's references are resolved relative to its own path. Two aliases of the
            // same model can resolve different textures and cannot share one relocated bundle.
            if (isModel && uses.Where(use => use.Source is ModelSource).Any(use =>
                    use.Uri.LocalPath != first.Uri.LocalPath || !use.Files.ToHashSet(StringComparer.Ordinal).SetEquals(first.Files)))
                issue ??= ProjectAssetIssue.UnsafePath;

            ProjectAssetFile[] files;
            if (isModel)
            {
                (string root, string[] modelFiles) = ResourceRelocationService.GetModelFileLayout(mainPath, first.Files);
                files = [.. modelFiles.Select(file => PlanFile(file, Path.Combine(destination, Path.GetRelativePath(root, file))))];
            }
            else
            {
                files = [PlanFile(mainPath, destination)];
            }

            if (files.Any(file => file.Stamp == null)) issue ??= ProjectAssetIssue.Missing;
            return new(files, issue);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new([new(mainPath, destination, null)], ProjectAssetIssue.Unavailable);
        }
    }

    private static string FindDestination(string directory, string name, HashSet<string> planned)
    {
        string path = Path.Combine(directory, name);
        for (int suffix = 1; ProjectAssetPaths.GetAttributes(path) != null || !planned.Add(path); suffix++)
            path = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(name)}_{suffix}{Path.GetExtension(name)}");
        return path;
    }

    private static ProjectAssetFile PlanFile(string source, string destination)
        => new(source, destination, ProjectAssetStamp.Read(source));

    private static ProjectAssetItem PlanMove(string source, string destination, ProjectAssetIssue? issue)
    {
        try
        {
            ProjectAssetFile file = PlanFile(source, destination);
            return new([file], issue ?? (file.Stamp == null ? ProjectAssetIssue.Missing : null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new([new(source, destination, null)], issue ?? ProjectAssetIssue.Unavailable);
        }
    }

    private static bool HasExtension(string path, string extension) =>
        Path.GetExtension(path).Equals("." + extension, StringComparison.OrdinalIgnoreCase);

    private static bool IsProjectDocument(string path) => HasExtension(path, EditorConstants.ProjectFileExtension)
        || HasExtension(path, EditorConstants.SceneFileExtension) || HasExtension(path, EditorConstants.ElementFileExtension);

    // Saved files can still mention a source removed in an unsaved edit, and extension data may
    // store paths in members the object visitor cannot see. Compare literal file references too.
    private static void ReadSavedReferences(IEnumerable<string> documents, HashSet<string> used,
        FilePathComparison.ResolutionContext canonical, ProjectAssetPaths paths, CancellationToken token)
    {
        var pending = new Queue<string>(documents);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        // Relative dependencies depend on the model's path, including any symlink aliases.
        var visitedModels = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out string? document))
        {
            token.ThrowIfCancellationRequested();
            if (!visited.Add(canonical.ResolveCanonicalPath(document)) || ProjectAssetPaths.GetAttributes(document) == null) continue;
            using var stream = new FileStream(document, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var json = JsonDocument.Parse(stream);
            Uri baseUri = UriHelper.CreateFromPath(document);
            Visit(json.RootElement, baseUri, HasExtension(document, EditorConstants.SceneFileExtension));
        }

        void Visit(JsonElement node, Uri baseUri, bool sceneRoot = false, bool modelReference = false)
        {
            switch (node.ValueKind)
            {
                case JsonValueKind.Object:
                    bool isModel = node.TryGetProperty("$type", out JsonElement type)
                        && type.ValueKind == JsonValueKind.String && type.ValueEquals(s_modelType);
                    foreach (JsonProperty member in node.EnumerateObject())
                    {
                        // The scene's include/exclude values are globs. The element files are
                        // already in the project scan and the live scene's document list.
                        if (sceneRoot && member.NameEquals("Elements") && member.Value.ValueKind == JsonValueKind.Object)
                            continue;
                        Visit(member.Value, baseUri, modelReference: isModel && member.NameEquals(nameof(Model3D.Source)));
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement child in node.EnumerateArray()) Visit(child, baseUri);
                    break;
                case JsonValueKind.String when node.GetString() is { } value && (modelReference || value.Contains('.')):
                    Uri uri;
                    try
                    {
                        uri = UriHelper.ResolvePersistedReference(value, baseUri);
                    }
                    catch (JsonException) when (!modelReference)
                    {
                        break;
                    }

                    if (!uri.IsFile)
                    {
                        if (modelReference) throw new JsonException("A saved model source must reference a local file.");
                        break;
                    }

                    string file = uri.LocalPath;
                    if (IsProjectDocument(file)) pending.Enqueue(file);
                    string identity = canonical.ResolveCanonicalPath(file);
                    if (paths.IsInside(identity)) used.Add(identity);
                    // Also protect model files stored in extension data. A filename in an arbitrary
                    // string only counts as a model when it resolves to an existing file.
                    if ((modelReference || (s_modelExtensions.Contains(Path.GetExtension(file)) && File.Exists(file)))
                        && visitedModels.Add(file))
                    {
                        token.ThrowIfCancellationRequested();
                        used.UnionWith(ReadModelFiles(uri).Select(canonical.ResolveCanonicalPath));
                    }
                    break;
            }
        }
    }

    private static IEnumerable<string> ReadModelFiles(Uri uri)
    {
        var model = new ModelSource();
        try
        {
            model.ReadFrom(uri);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            throw new IOException($"Could not inspect the dependencies of model '{uri.LocalPath}'.", ex);
        }

        if (model.MeshCount == 0)
            throw new IOException($"Model '{uri.LocalPath}' contains no readable meshes.");
        return FileSourceFiles.EnumerateLocalPaths(model);
    }
}
