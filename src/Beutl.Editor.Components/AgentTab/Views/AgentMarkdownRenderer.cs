using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Logging;
using Beutl.Utilities;
using FluentIcons.Avalonia.Fluent;
using FluentIcons.Common;
using LiveMarkdown.Avalonia;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Extensions.Logging;
using CodeBlock = LiveMarkdown.Avalonia.CodeBlock;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed class AgentMarkdownRenderer : MarkdownRenderer
{
    private static readonly MarkdownPipeline s_pipeline = CreatePipeline();
    private static readonly ILogger s_logger = Log.CreateLogger<AgentMarkdownRenderer>();

    public AgentMarkdownRenderer()
    {
        UpdateProducer = new MarkdownUpdateProducer { Pipeline = s_pipeline };
        AddHandler(CodeBlock.CopyingToClipboardEvent, OnCopyCode);
    }

    protected override Type StyleKeyOverride => typeof(MarkdownRenderer);

    private static async void OnCopyCode(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not CodeBlock block || TopLevel.GetTopLevel(block)?.Clipboard is not { } clipboard)
            return;
        e.Handled = true;
        try
        {
            await clipboard.SetTextAsync(block.Inlines.Text);
            if (block.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Name == "PART_CopyButton")
                is { Content: FluentIcon { Icon: Icon.Copy } icon } copy)
            {
                icon.Icon = Icon.Checkmark;
                await Task.Delay(TimeSpan.FromSeconds(1));
                if (copy.Content == icon)
                    icon.Icon = Icon.Copy;
            }
        }
        catch (Exception ex) when (!ExceptionHelpers.ContainsFatalFailure(ex))
        {
            s_logger.LogWarning(ex, "Could not copy an agent code block.");
        }
    }

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
