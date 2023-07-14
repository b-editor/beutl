using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Framework;
using Beutl.ViewModels;
using Beutl.ViewModels.NodeTree;
using Beutl.Views.NodeTree;

namespace Beutl.Services.PrimitiveImpls;

public sealed class NodeTreeTabExtension : ToolTabExtension
{
    public static readonly NodeTreeTabExtension Instance = new();

    public override bool CanMultiple => true;

    public override string Name => "NodeTree";

    public override string DisplayName => "NodeTree";

    public override string? Header => "Node Tree";

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new NodeTreeTab();
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
            context = new NodeTreeTabViewModel(editViewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
