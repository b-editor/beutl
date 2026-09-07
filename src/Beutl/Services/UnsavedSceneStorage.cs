using Beutl.Editor;
using Beutl.IO;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal static class UnsavedSceneStorage
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(UnsavedSceneStorage));

    public static string GetDirectory(Guid sceneId)
        => Path.Combine(
            BeutlEnvironment.GetHomeDirectoryPath(),
            "tmp",
            "unsaved",
            sceneId.ToString("N"));

    public static string GetElementDirectory(Guid sceneId)
        => Path.Combine(GetDirectory(sceneId), "elements");

    public static bool OwnsPath(Guid sceneId, string path)
    {
        string root = Path.GetFullPath(GetDirectory(sceneId));
        string candidate = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(root, candidate);
        return !Path.IsPathRooted(relative)
            && !string.Equals(relative, "..", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    public static void Cleanup(Guid sceneId)
    {
        string directory = GetDirectory(sceneId);
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(
                ex,
                "Failed to remove temporary resources owned by scene {SceneId} from {Path}.",
                sceneId,
                directory);
        }
    }

    public static SaveRelocation PrepareSave(Scene scene, Uri sceneUri)
        => new(scene, sceneUri);

    internal sealed class SaveRelocation
    {
        private static readonly ILogger s_logger = Log.CreateLogger<SaveRelocation>();
        private readonly Scene _scene;
        private readonly List<ElementRehome> _elements;
        private readonly List<ResourceRehome> _resources;

        public SaveRelocation(Scene scene, Uri sceneUri)
        {
            _scene = scene;
            _elements = CreateElementRehomes(scene, sceneUri);
            _resources = CreateResourceRehomes(scene, sceneUri);
        }

        public void Apply()
        {
            foreach (ResourceRehome rehome in _resources)
            {
                CopyFileAtomically(rehome.Original.LocalPath, rehome.Destination.LocalPath);
            }
            foreach (ResourceRehome rehome in _resources)
            {
                foreach (IFileSource source in rehome.Sources)
                    source.ReadFrom(rehome.Destination);
            }
            foreach (ElementRehome rehome in _elements)
            {
                CoreSerializer.StoreToUri(rehome.Element, rehome.Destination);
            }
        }

        public void Rollback()
        {
            foreach (ResourceRehome rehome in _resources)
            {
                foreach (IFileSource source in rehome.Sources)
                {
                    try
                    {
                        source.ReadFrom(rehome.Original);
                    }
                    catch (Exception ex)
                    {
                        s_logger.LogWarning(ex, "Failed to restore an AI resource URI after save failed.");
                    }
                }
                TryDelete(rehome.Destination.LocalPath);
            }
            foreach (ElementRehome rehome in _elements)
            {
                rehome.Element.Uri = rehome.Original;
                TryDelete(rehome.Destination.LocalPath);
            }
        }

        public void Commit()
        {
            foreach (ElementRehome rehome in _elements)
                TryDelete(rehome.Original.LocalPath);
            foreach (ResourceRehome rehome in _resources)
                TryDelete(rehome.Original.LocalPath);
            TryPruneDirectories(GetDirectory(_scene.Id));
        }

        private static List<ElementRehome> CreateElementRehomes(Scene scene, Uri sceneUri)
        {
            var result = new List<ElementRehome>();
            var destinations = new HashSet<string>(GetPathComparer());
            foreach (Element element in scene.Children)
            {
                if (element.Uri is not { IsFile: true } original
                    || !OwnsPath(scene.Id, original.LocalPath))
                {
                    continue;
                }

                Uri destination;
                do
                {
                    destination = RandomFileNameGenerator.GenerateUri(
                        sceneUri,
                        EditorConstants.ElementFileExtension);
                }
                while (!destinations.Add(destination.LocalPath));

                result.Add(new ElementRehome(element, original, destination));
            }
            return result;
        }

        private static List<ResourceRehome> CreateResourceRehomes(Scene scene, Uri sceneUri)
        {
            string destinationDirectory = Path.Combine(
                Path.GetDirectoryName(sceneUri.LocalPath)!,
                "resources",
                "ai");
            var groups = new Dictionary<string, List<IFileSource>>(GetPathComparer());
            var seen = new HashSet<IFileSource>(ReferenceEqualityComparer.Instance);
            foreach (IFileSource source in scene.Children
                         .SelectMany(element => ProxySourceEnumerator.EnumerateFileSources(element)))
            {
                if (!seen.Add(source))
                    continue;

                Uri original;
                try
                {
                    original = source.Uri;
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (!original.IsFile || !OwnsPath(scene.Id, original.LocalPath))
                    continue;

                if (!groups.TryGetValue(original.LocalPath, out List<IFileSource>? sources))
                {
                    sources = [];
                    groups.Add(original.LocalPath, sources);
                }
                sources.Add(source);
            }

            var destinations = new HashSet<string>(GetPathComparer());
            var result = new List<ResourceRehome>(groups.Count);
            foreach ((string sourcePath, List<IFileSource> sources) in groups)
            {
                if (!File.Exists(sourcePath))
                    throw new FileNotFoundException("An unsaved scene resource is missing.", sourcePath);

                Directory.CreateDirectory(destinationDirectory);
                string destination = GetUniqueDestination(
                    destinationDirectory,
                    Path.GetFileName(sourcePath),
                    destinations);
                result.Add(new ResourceRehome(
                    UriHelper.CreateFromPath(sourcePath),
                    UriHelper.CreateFromPath(destination),
                    sources));
            }
            return result;
        }

        private static string GetUniqueDestination(
            string directory,
            string fileName,
            ISet<string> reserved)
        {
            string stem = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            for (int suffix = 0; ; suffix++)
            {
                string candidateName = suffix == 0
                    ? fileName
                    : $"{stem}-{suffix}{extension}";
                string candidate = Path.Combine(directory, candidateName);
                if (!File.Exists(candidate) && reserved.Add(candidate))
                    return candidate;
            }
        }

        private static void CopyFileAtomically(string source, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.Copy(source, temporary, overwrite: false);
                File.Move(temporary, destination, overwrite: false);
            }
            catch
            {
                TryDelete(temporary);
                throw;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(ex, "Failed to remove obsolete unsaved-scene file {Path}.", path);
            }
        }

        private static void TryPruneDirectories(string root)
        {
            try
            {
                if (!Directory.Exists(root))
                    return;

                foreach (string directory in Directory
                             .EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                             .OrderByDescending(path => path.Length))
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                        Directory.Delete(directory);
                }
                if (!Directory.EnumerateFileSystemEntries(root).Any())
                    Directory.Delete(root);
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(ex, "Failed to prune unsaved-scene storage {Path}.", root);
            }
        }

        private static StringComparer GetPathComparer()
            => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        private sealed record ElementRehome(Element Element, Uri Original, Uri Destination);

        private sealed record ResourceRehome(
            Uri Original,
            Uri Destination,
            IReadOnlyList<IFileSource> Sources);
    }
}
