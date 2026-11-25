using Beutl.Graphics3D.Models;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor;

internal sealed class ProjectAssetPlanChangedException : IOException;

internal sealed class ProjectAssetDeleteCanceledException(int deletedCount, CancellationToken token)
    : OperationCanceledException(token)
{
    internal int DeletedCount { get; } = deletedCount;
}

internal sealed class ProjectAssetValidationException(string path, Exception cause)
    : IOException($"Could not validate the copy of {path}: {cause.Message}", cause)
{
    internal string Path { get; } = path;
}

internal sealed partial class ProjectAssetService
{
    private static readonly ILogger s_logger = Log.CreateLogger<ProjectAssetService>();
    private readonly MissingMediaService _validator = new(static _ => false, decoders);

    internal async Task<IReadOnlyCollection<CoreObject>> ApplyAsync(Project project, ProjectAssetPaths paths, Analysis current, ProjectAssetAction action,
        IReadOnlyList<ProjectAssetItem> selected, CancellationToken token)
    {
        var rows = current.Preview.ForAction(action).ToDictionary(item => item.Path, StringComparer.Ordinal);
        var approved = new List<ProjectAssetItem>(selected.Count);
        foreach (ProjectAssetItem item in selected.DistinctBy(item => item.Path))
        {
            if (!rows.TryGetValue(item.Path, out ProjectAssetItem? latest) || !latest.CanApply(action)
                || !latest.Files.SequenceEqual(item.Files))
                throw new ProjectAssetPlanChangedException();
            approved.Add(latest);
        }

        if (approved.Count == 0) return [];
        if (action == ProjectAssetAction.Collect)
        {
            return await CollectAsync(project, paths, approved, current.Sources, token);
        }
        if (action == ProjectAssetAction.Delete)
        {
            await Task.Run(() => Delete(paths, approved, token), token);
        }
        else
        {
            await Task.Run(() => Move(paths, approved, token), token);
        }
        return [];
    }

    private async Task<IReadOnlyCollection<CoreObject>> CollectAsync(Project project, ProjectAssetPaths paths, List<ProjectAssetItem> items,
        Dictionary<string, List<Relink>> sources, CancellationToken token)
    {
        var temporary = new List<(string Path, ProjectAssetFile File)>();
        var published = new List<string>();
        var createdDirectories = new Stack<string>();
        bool preserveCopies = false;
        try
        {
            await Task.Run(async () =>
            {
                foreach (ProjectAssetFile file in items.SelectMany(item => item.Files))
                {
                    token.ThrowIfCancellationRequested();
                    RequireUnchanged(file);
                    paths.RequireWritable(file.Destination);
                    CreateParentDirectories(file.Destination, createdDirectories);
                    string stagingPath = $"{file.Destination}.{Guid.NewGuid():N}.tmp";
                    temporary.Add((stagingPath, file));
                    await ResourceRelocationService.CopyFileAsync(file.Path, stagingPath, token, flushToDisk: true);
                    RequireUnchanged(file);
                    if (new FileInfo(stagingPath).Length != file.Stamp!.Value.Length)
                        throw new IOException($"The copied file has changed length: {file.Path}");
                }

                foreach ((string stagingPath, ProjectAssetFile file) in temporary)
                {
                    token.ThrowIfCancellationRequested();
                    paths.RequireWritable(file.Destination);
                    RequireUnchanged(file);
                    File.Move(stagingPath, file.Destination);
                    published.Add(file.Destination);
                }
            }, token);

            var models = new Dictionary<string, ModelSource>(StringComparer.Ordinal);
            foreach (ProjectAssetItem item in items)
            {
                foreach (var link in sources[item.Path]
                             .Select(link => (Kind: MissingMediaService.GetKind(link.Use.Source), link.Destination, link.Use.Uri))
                             .Where(link => link.Kind != null).DistinctBy(link => (link.Kind, link.Destination)))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var media = new MissingMedia(link.Kind!.Value, link.Uri, null, [], null);
                        if (await _validator.ValidateAsync(media, link.Destination, token) is ModelSource model)
                        {
                            var expected = item.Files.Skip(1).Select(file => FilePathComparison.ResolveCanonicalPath(file.Destination))
                                .ToHashSet(StringComparer.Ordinal);
                            string main = FilePathComparison.ResolveCanonicalPath(item.Destination);
                            if (!expected.SetEquals(model.Dependencies.Select(FilePathComparison.ResolveCanonicalPath).Where(file => file != main)))
                                throw new IOException("The copied model does not resolve all of its dependencies inside the project.");
                            models.Add(link.Destination, model);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        throw new ProjectAssetValidationException(link.Uri.LocalPath, ex);
                    }
                }
            }

            var documents = new HashSet<CoreObject>(ReferenceEqualityComparer.Instance) { project };
            foreach (CoreObject document in items.SelectMany(item => sources[item.Path]).SelectMany(link => link.Use.Documents))
                AutoSaveService.AddObjectWithAncestors(document, documents);
            foreach (CoreObject document in documents)
                paths.RequireWritable(document.Uri!.LocalPath);

            var sceneFingerprints = documents.OfType<Scene>().ToDictionary(scene => scene,
                scene => scene.MediaFingerprints.ToArray());
            var changed = new Stack<(ProjectAssetReferences.Use Use, ModelSource? OriginalModel)>();
            using var transaction = StorageWriteTransaction.Begin();
            try
            {
                foreach (ProjectAssetItem item in items)
                {
                    foreach ((ProjectAssetReferences.Use use, string path) in sources[item.Path])
                    {
                        token.ThrowIfCancellationRequested();
                        if (use.Source.Uri != use.Uri) throw new ProjectAssetPlanChangedException();
                        changed.Push((use, (use.Source as ModelSource)?.CaptureState()));
                        ResourceRelocationService.RelinkFileSource(use.Source, UriHelper.CreateFromPath(path), models.GetValueOrDefault(path));
                    }
                }

                await StoreDocumentsAsync(paths, documents, items.SelectMany(item => item.Files), token);
                transaction.Commit();
                preserveCopies = true;
            }
            catch
            {
                // If a document could not be rolled back, its on-disk URI may still point at a copy.
                preserveCopies = !transaction.Rollback();
                while (changed.TryPop(out var change))
                {
                    try
                    {
                        ResourceRelocationService.RelinkFileSource(change.Use.Source, change.Use.Uri, change.OriginalModel);
                    }
                    catch (Exception ex)
                    {
                        preserveCopies = true;
                        s_logger.LogError(ex, "Could not restore the source at {Uri} after a failed collection.", change.Use.Uri);
                    }
                }
                foreach ((Scene scene, var fingerprints) in sceneFingerprints) scene.SetMediaFingerprints(fingerprints);
                throw;
            }
            return documents;
        }
        finally
        {
            await Task.Run(() =>
            {
                foreach ((string path, _) in temporary) TryDeleteFile(paths, path);
                if (!preserveCopies)
                {
                    foreach (string path in published) TryDeleteFile(paths, path);
                    RemoveEmptyDirectories(paths, createdDirectories);
                }
            });
        }
    }

    private static Task StoreDocumentsAsync(ProjectAssetPaths paths, HashSet<CoreObject> documents,
        IEnumerable<ProjectAssetFile> sources, CancellationToken token)
    {
        var pending = new HashSet<string>(StringComparer.Ordinal);
        var writes = new List<(string Path, Action Write)>();
        foreach (CoreObject document in documents.OrderBy(document => document is Project ? 2 : document is Scene ? 1 : 0))
        {
            token.ThrowIfCancellationRequested();
            string path = document.Uri!.LocalPath;
            writes.Add((path, CoreSerializer.PrepareStoreToUri(document, pending)));
            pending.Add(path);
        }

        return Task.Run(() =>
        {
            foreach ((string path, Action write) in writes)
            {
                token.ThrowIfCancellationRequested();
                paths.RequireWritable(path);
                write();
            }
            foreach (ProjectAssetFile file in sources)
            {
                token.ThrowIfCancellationRequested();
                RequireUnchanged(file);
            }
        }, token);
    }

    private static void Delete(ProjectAssetPaths paths, List<ProjectAssetItem> items, CancellationToken token)
    {
        int deleted = 0;
        foreach (ProjectAssetFile file in items.SelectMany(item => item.Files))
        {
            if (token.IsCancellationRequested) throw new ProjectAssetDeleteCanceledException(deleted, token);
            paths.RequireWritable(file.Path);
            RequireUnchanged(file);
            File.Delete(file.Path);
            deleted++;
        }
    }

    private static void Move(ProjectAssetPaths paths, List<ProjectAssetItem> items, CancellationToken token)
    {
        var moved = new Stack<ProjectAssetFile>();
        var createdDirectories = new Stack<string>();
        try
        {
            foreach (ProjectAssetFile file in items.SelectMany(item => item.Files))
            {
                token.ThrowIfCancellationRequested();
                paths.RequireWritable(file.Path);
                RequireUnchanged(file);
                paths.RequireWritable(file.Destination);
                CreateParentDirectories(file.Destination, createdDirectories);
                File.Move(file.Path, file.Destination);
                moved.Push(file);
            }
        }
        catch (Exception failure)
        {
            var recoveryFailures = new List<Exception>();
            while (moved.TryPop(out ProjectAssetFile file))
            {
                try
                {
                    paths.RequireWritable(file.Destination);
                    paths.RequireWritable(file.Path);
                    File.Move(file.Destination, file.Path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    recoveryFailures.Add(new IOException($"The file is still stored at {file.Destination}.", ex));
                }
            }

            RemoveEmptyDirectories(paths, createdDirectories);
            if (recoveryFailures.Count > 0) throw new AggregateException([failure, .. recoveryFailures]);
            throw;
        }
    }

    private static void RequireUnchanged(ProjectAssetFile file)
    {
        if (ProjectAssetStamp.Read(file.Path) != file.Stamp || file.Stamp == null)
            throw new ProjectAssetPlanChangedException();
    }

    private static void CreateParentDirectories(string file, Stack<string> created)
    {
        string directory = Path.GetDirectoryName(file) ?? throw new IOException($"No parent directory for '{file}'.");
        var missing = new Stack<string>();
        string parent = directory;
        while (!Directory.Exists(parent))
        {
            missing.Push(parent);
            parent = Path.GetDirectoryName(parent) ?? throw new IOException($"No existing parent directory for '{directory}'.");
        }
        Directory.CreateDirectory(directory);
        foreach (string missingDirectory in missing) created.Push(missingDirectory);
    }

    private static void RemoveEmptyDirectories(ProjectAssetPaths paths, Stack<string> directories)
    {
        foreach (string directory in directories)
        {
            try
            {
                if (paths.IsWritable(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                s_logger.LogDebug(ex, "Could not remove the empty asset directory {Path}.", directory);
            }
        }
    }

    private static void TryDeleteFile(ProjectAssetPaths paths, string path)
    {
        try
        {
            if (paths.IsWritable(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            s_logger.LogWarning(ex, "Could not remove the uncommitted asset at {Path}.", path);
        }
    }
}
