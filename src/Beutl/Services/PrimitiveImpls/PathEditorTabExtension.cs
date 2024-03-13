using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.ViewModels;
using Beutl.Views.Tools;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class PathEditorTabExtension : ToolTabExtension
{
    public static readonly PathEditorTabExtension Instance = new();

    public override string Name => "PathEditor";

    public override string DisplayName => "PathEditor";

    public override bool CanMultiple => false;

    public override string? Header => Strings.PathEditor;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new PathEditorTab();
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
            context = new PathEditorTabViewModel(editViewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
