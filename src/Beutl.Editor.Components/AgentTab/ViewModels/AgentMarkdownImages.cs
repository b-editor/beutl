using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal static class AgentMarkdownImages
{
    private static readonly MarkdownPipeline s_pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

    public static IReadOnlyList<AgentImagePreview> GetPreviews(string text, string? projectDirectory)
    {
        if (!text.Contains("![", StringComparison.Ordinal))
            return [];

        var images = new List<AgentImagePreview>();
        foreach (var image in Markdown.Parse(text, s_pipeline).Descendants<LinkInline>())
        {
            if (!image.IsImage || AgentMarkdownLocalPath.Decode(image.Url, allowAbsolutePath: true) is not { } destination
                || AgentMarkdownLocalPath.Resolve(destination, projectDirectory) is not { IsDirectory: false, FullPath: var path })
                continue;

            string alt = string.Concat(image.Descendants<LiteralInline>().Select(inline => inline.Content.ToString())).Trim();
            if (AgentImagePreview.FromFile(path, alt.Length > 0 ? alt : Path.GetFileName(path), AgentMarkdownLocalPath.IsLocalImageFile) is { } preview)
                images.Add(preview);
        }
        return images;
    }
}
