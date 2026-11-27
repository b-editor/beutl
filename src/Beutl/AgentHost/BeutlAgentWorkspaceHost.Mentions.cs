using Beutl.Agents;
using Beutl.Editor.Components.AgentTab;
using Beutl.ProjectSystem;
using Beutl.ViewModels;

namespace Beutl.AgentHost;

internal sealed partial class BeutlAgentWorkspaceHost
{
    private static readonly HashSet<string> s_ignoredAssetDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules"
    };

    public async Task<IReadOnlyList<AgentMentionCandidate>> GetMentionCandidatesAsync(
        IEditorContext editor, string projectPath, CancellationToken cancellationToken)
    {
        var edit = (EditViewModel)editor;
        EnsureEditorAvailable(edit);
        var activeScene = edit.Scene;
        var project = activeScene.FindHierarchicalParent<Project>();
        var candidates = new List<AgentMentionCandidate>();
        foreach (var scene in project?.Items.OfType<Scene>() ?? [activeScene])
        {
            string? path = GetLocalPath(scene);
            string name = scene.Name.Value;
            if (string.IsNullOrWhiteSpace(name))
                name = path is null ? AgentStrings.MentionScene : Path.GetFileNameWithoutExtension(path);
            string id = scene.Id.Value.ToString("D");
            var size = scene.FrameSize.Value;
            double fps = scene.FrameRate.Value.ToDouble();
            double duration = scene.Duration.Value.ToSeconds();
            string contextDetail = FormattableString.Invariant($"{size.Width} × {size.Height} · {fps:R} fps · {duration:R} s");
            string displayDetail = string.Format(AgentStrings.MentionSceneDetail, size.Width, size.Height, fps, duration);
            candidates.Add(new(new(AgentContextMentionKind.Scene, name, id, path, contextDetail), displayDetail));
        }

        foreach (var element in activeScene.Children)
        {
            var range = element.Range;
            string displayDetail = string.Format(AgentStrings.MentionElementDetail,
                range.Start.ToSeconds(), range.End.ToSeconds(), element.ZIndex.Value);
            candidates.Add(new(AgentComposerLauncher.CreateElementMention(element), displayDetail));
        }

        var files = await Task.Run(() => DiscoverAssetMentions(Path.GetDirectoryName(projectPath), cancellationToken), cancellationToken);
        candidates.AddRange(files);
        return candidates;
    }

    private static List<AgentMentionCandidate> DiscoverAssetMentions(string? root, CancellationToken cancellationToken)
    {
        const int maxFiles = 200;
        const int maxEntries = 2000;
        var candidates = new List<AgentMentionCandidate>();
        if (string.IsNullOrEmpty(root))
            return candidates;

        var options = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
            IgnoreInaccessible = true
        };
        var directories = new Queue<DirectoryInfo>();
        directories.Enqueue(new DirectoryInfo(root));
        int entries = 0;
        while (directories.TryDequeue(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // The directory may have changed since the parent was enumerated.
                directory.Refresh();
                if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                foreach (var entry in directory.EnumerateFileSystemInfos("*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++entries > maxEntries)
                        return candidates;
                    if (entry.Name.StartsWith('.'))
                        continue;
                    if (entry is DirectoryInfo child)
                    {
                        if (!s_ignoredAssetDirectories.Contains(child.Name))
                            directories.Enqueue(child);
                        continue;
                    }

                    string relativePath = Path.GetRelativePath(root, entry.FullName).Replace(Path.DirectorySeparatorChar, '/');
                    candidates.Add(new(new(AgentContextMentionKind.File, entry.Name, Path: entry.FullName,
                        Detail: relativePath), relativePath));
                    if (candidates.Count == maxFiles)
                        return candidates;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keep the results collected from the rest of the project.
            }
        }
        return candidates;
    }

    private static string? GetLocalPath(CoreObject item) => item.Uri is { IsFile: true } uri ? uri.LocalPath : null;
}
