using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Logging;
using Beutl.Utilities;
using FluentAvalonia.UI.Controls;
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

    internal static Action<string> LaunchUrl { get; set; } = DefaultLaunchUrl;

    public AgentMarkdownRenderer()
    {
        UpdateProducer = new MarkdownUpdateProducer { Pipeline = s_pipeline };
        AddHandler(CodeBlock.CopyingToClipboardEvent, OnCopyCode);
        LinkClick += OnLinkClick;
    }

    protected override Type StyleKeyOverride => typeof(MarkdownRenderer);

    private async void OnLinkClick(object? sender, LinkClickedEventArgs e)
    {
        e.Handled = true;
        try
        {
            if (e.HRef is { IsAbsoluteUri: true } uri && uri.Scheme is "https" or "http")
                await OpenExternalLinkAsync(uri);
            else if (AgentMarkdownLink.Parse(e.HRef) is { } link && this.FindAncestorOfType<AgentTimelineView>() is { } timeline)
                await timeline.OpenLinkAsync(link);
        }
        catch (Exception ex) when (!ExceptionHelpers.ContainsFatalFailure(ex))
        {
            s_logger.LogWarning(ex, "Could not open an agent Markdown link.");
        }
    }

    private async Task OpenExternalLinkAsync(Uri uri)
    {
        if (TopLevel.GetTopLevel(this) is not { } owner) return;

        string url = uri.AbsoluteUri;
        var dialog = new FAContentDialog
        {
            Title = ExtensionsStrings.OpenUrl_Title,
            Content = new SelectableTextBlock
            {
                Text = string.Format(CultureInfo.CurrentCulture, ExtensionsStrings.OpenUrl_Content, url),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            },
            PrimaryButtonText = Strings.Open,
            CloseButtonText = Strings.Cancel
        };

        if (await dialog.ShowAsync(owner) is FAContentDialogResult.Primary)
            LaunchUrl(url);
    }

    private static void DefaultLaunchUrl(string url)
        => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true, Verb = "open" });

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
            // Only independently validated local files get thumbnails. Never hand an
            // agent-provided image destination to the Markdown image loader.
            foreach (var image in document.Descendants<LinkInline>().Where(link => link.IsImage).ToArray())
            {
                image.MoveChildrenAfter(image);
                image.Remove();
            }
        };
        return builder.Build();
    }
}
