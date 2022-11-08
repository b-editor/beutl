using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Framework;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class StreamOperatorsTabExtension : ToolTabExtension
{
    public static readonly StreamOperatorsTabExtension Instance = new();

    public override bool CanMultiple => false;

    public override string Name => "Stream operators editor";

    public override string DisplayName => "Stream operators editor";

    public override string? Header => Strings.StreamOperators;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out IControl? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new StreamOperatorsTab();
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
            context = new StreamOperatorsTabViewModel(editViewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
