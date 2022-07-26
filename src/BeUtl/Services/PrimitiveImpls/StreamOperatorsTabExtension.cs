using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using BeUtl.Framework;
using BeUtl.ViewModels;
using BeUtl.ViewModels.Editors;
using BeUtl.Views.Editors;

namespace BeUtl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class StreamOperatorsTabExtension : ToolTabExtension
{
    public static readonly StreamOperatorsTabExtension Instance = new();

    public override bool CanMultiple => false;

    public override string Name => "Stream operators editor";

    public override string DisplayName => "Stream operators editor";

    public override ResourceReference<string>? Header => "S.Common.StreamOperators";

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out IControl? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new StreamOperatorsEditor();
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
            context = new StreamOperatorsEditorViewModel(editViewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
