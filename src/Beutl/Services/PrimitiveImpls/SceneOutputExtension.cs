using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using FluentAvalonia.UI.Controls;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class SceneOutputExtension : OutputExtension
{
    public static readonly SceneOutputExtension Instance = new();

    public override string Name => "Scene Output";

    public override string DisplayName => "Scene Output";

    public override FilePickerFileType GetFilePickerFileType()
    {
        return new FilePickerFileType(Strings.SceneFile)
        {
            Patterns = new string[]
            {
                "*.scene"
            }
        };
    }

    public override IconSource? GetIcon()
    {
        return new SymbolIconSource
        {
            Symbol = Symbol.Document
        };
    }

    public override bool MatchFileExtension(string ext)
    {
        return string.Equals(ext, ".scene", StringComparison.OrdinalIgnoreCase);
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IOutputContext? context)
    {
        if (editorContext is EditViewModel editViewModel)
        {
            context = new OutputViewModel(editViewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }

    public override bool TryCreateControl(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new OutputView();
            return true;
        }
        else
        {
            control = null;
            return false;
        }
    }
}
