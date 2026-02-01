using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using FluentAvalonia.UI.Controls;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class FileBrowserTabExtension : ToolTabExtension
{
    public static readonly FileBrowserTabExtension Instance = new();

    public override string Name => "FileBrowser";

    public override string DisplayName => Strings.FileBrowser;

    public override string? Header => Strings.FileBrowser;

    public override bool CanMultiple => false;

    public override IconSource GetIcon()
    {
        return new SymbolIconSource { Symbol = Symbol.Folder };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new FileBrowserTab();
            return true;
        }
        else
        {
            control = null;
            return false;
        }
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        if (editorContext is EditViewModel editViewModel)
        {
            context = new FileBrowserTabViewModel(editViewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
