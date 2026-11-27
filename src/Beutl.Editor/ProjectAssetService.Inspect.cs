using Beutl.Editor.Services;
using Beutl.Graphics3D.Models;
using Beutl.IO;

namespace Beutl.Editor;

internal sealed partial class ProjectAssetService
{
    internal sealed record AssetInfo(ProjectAssetItem Item, string Kind, bool IsInsideProject, Guid[] ElementIds);

    internal sealed record Inspection(
        AssetInfo[] External, AssetInfo[] Unused, AssetInfo[] Quarantined,
        AssetInfo[] ReferencedInsideProject, ProjectAssetIssue? CleanupIssue);

    internal Inspection Inspect(ProjectAssetPaths paths, ProjectAssetReferences references, CancellationToken token)
    {
        Analysis analysis = Analyze(paths, references, Guid.NewGuid(), token);
        FilePathComparison.ResolutionContext canonical = FilePathComparison.CreateResolutionContext();
        var externalSources = analysis.Sources.Values.SelectMany(links => links)
            .Select(link => link.Use.Source).ToHashSet<IFileSource>(ReferenceEqualityComparer.Instance);

        AssetInfo[] inside = [.. references.Sources.Values.Where(use => !externalSources.Contains(use.Source))
            .GroupBy(use => canonical.ResolveCanonicalPath(use.Uri.LocalPath))
            .Select(group =>
            {
                var uses = group.ToArray();
                string main = (uses.FirstOrDefault(use => use.Source is ModelSource) ?? uses[0]).Uri.LocalPath;
                var files = uses.SelectMany(use => use.Files).Prepend(main).Distinct(StringComparer.Ordinal)
                    .Select(path => PlanMove(path, path, null).Files[0]).ToArray();
                return Describe(new ProjectAssetItem(files), uses);
            })
            .OrderBy(asset => asset.Item.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(asset => asset.Item.Path, StringComparer.Ordinal)];

        ProjectAssetPreview preview = analysis.Preview;
        return new(
            [.. preview.External.Select(item => Describe(item, analysis.Sources[item.Path].Select(link => link.Use)))],
            [.. preview.Unused.Select(item => Describe(item, []))],
            [.. preview.Quarantined.Select(item => Describe(item, []))],
            inside, preview.CleanupIssue);

        AssetInfo Describe(ProjectAssetItem item, IEnumerable<ProjectAssetReferences.Use> uses)
        {
            token.ThrowIfCancellationRequested();
            var kind = uses.Select(use => MissingMediaService.GetKind(use.Source))
                .OrderByDescending(value => value == MissingMediaKind.Model).FirstOrDefault(value => value.HasValue)
                ?? InferFileKind(item.Path);
            return new(item, kind?.ToString().ToLowerInvariant() ?? "file",
                paths.IsInside(item.Path) && paths.IsInside(canonical.ResolveCanonicalPath(item.Path)),
                [.. uses.SelectMany(use => use.ElementIds ?? []).Distinct().Order()]);
        }
    }

    private MissingMediaKind? InferFileKind(string path)
    {
        string extension = Path.GetExtension(path);
        if (s_modelExtensions.Contains(extension) || extension.Equals(".mtl", StringComparison.OrdinalIgnoreCase))
            return MissingMediaKind.Model;
        if (extension.Equals(".cube", StringComparison.OrdinalIgnoreCase))
            return MissingMediaKind.Cube;
        if (MediaFileMatcher.IsImage(path)) return MissingMediaKind.Image;
        if (MediaFileMatcher.Matches(path, MediaFileMatcher.VideoExtensions(decoders))) return MissingMediaKind.Video;
        if (MediaFileMatcher.Matches(path, MediaFileMatcher.AudioExtensions(decoders))) return MissingMediaKind.Sound;
        return null;
    }
}
