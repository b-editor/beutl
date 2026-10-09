using Avalonia.Data.Converters;
using Avalonia.Styling;
using FluentAvalonia.Styling;
using LiveMarkdown.Avalonia;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using TextMateSharp.Grammars;

namespace Beutl.Converters;

public static class MarkdownConverters
{
    private static readonly MarkdownPipeline s_packagePipeline = CreatePackagePipeline();

    // An empty builder publishes an empty document, clearing any previously rendered content.
    public static readonly IValueConverter ToProducer = new FuncValueConverter<string?, MarkdownUpdateProducer>(
        text => new MarkdownUpdateProducer
        {
            Pipeline = s_packagePipeline,
            MarkdownBuilder = new ObservableStringBuilder(text),
        });

    public static readonly IValueConverter CodeBlockTheme = new FuncValueConverter<ThemeVariant?, ThemeName>(
        theme => theme == ThemeVariant.Light || theme == FluentAvaloniaTheme.HighContrastTheme
            ? ThemeName.LightPlus
            : ThemeName.DarkPlus);

    private static MarkdownPipeline CreatePackagePipeline()
    {
        var builder = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseCodeBlockSpanFixer().DisableHtml();
        builder.DocumentProcessed += document =>
        {
            // Publisher-controlled image URLs must never reach the renderer's image loader.
            // Keep their formatted alt text, including images nested inside ordinary links.
            foreach (LinkInline image in document.Descendants<LinkInline>().Where(link => link.IsImage).ToArray())
            {
                image.MoveChildrenAfter(image);
                image.Remove();
            }
        };
        return builder.Build();
    }
}
