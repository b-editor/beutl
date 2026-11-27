using LiveMarkdown.Avalonia;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed class AgentMarkdownRenderer : MarkdownRenderer
{
    private static readonly MarkdownPipeline s_pipeline = CreatePipeline();

    public AgentMarkdownRenderer()
    {
        UpdateProducer = new MarkdownUpdateProducer { Pipeline = s_pipeline };
    }

    protected override Type StyleKeyOverride => typeof(MarkdownRenderer);

    private static MarkdownPipeline CreatePipeline()
    {
        var builder = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseCodeBlockSpanFixer().DisableHtml();
        builder.DocumentProcessed += document =>
        {
            // Agent-provided Markdown can include tracking URLs. Tool images use the
            // explicit image output path instead of the Markdown image loader.
            foreach (var image in document.Descendants<LinkInline>().Where(link => link.IsImage).ToArray())
            {
                image.MoveChildrenAfter(image);
                image.Remove();
            }
        };
        return builder.Build();
    }
}
