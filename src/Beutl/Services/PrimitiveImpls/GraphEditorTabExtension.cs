using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Framework;
using Beutl.ViewModels;
using Beutl.Views.Tools;

namespace Beutl.Services.PrimitiveImpls;

public sealed class GraphEditorTabExtension : ToolTabExtension
{
    public static readonly GraphEditorTabExtension Instance = new();

    public override bool CanMultiple => true;

    public override string Name => "Graph Editor Tab";

    public override string DisplayName => "Graph Editor Tab";

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out IControl? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new GraphEditorTab();
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
        context = null;
        return false;
    }
}
