using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.ViewModels;
using Beutl.Views;
using FluentAvalonia.UI.Controls;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class FileExplorerExtension : ToolTabExtension
{
    public static readonly FileExplorerExtension Instance = new();

    public override string Name => "FileExplorer";

    public override string DisplayName => Strings.FileExplorer;

    public override bool CanMultiple => false;

    public override string? Header => Strings.FileExplorer;

    public override IconSource GetIcon()
    {
        return new SymbolIconSource { Symbol = Symbol.FolderOpen };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new FileExplorer();
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
            context = new FileExplorerViewModel(editViewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}