using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using BeUtl.Framework;
using BeUtl.ViewModels;
using BeUtl.ViewModels.Editors;
using BeUtl.Views.Editors;

namespace BeUtl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class ObjectPropertyTabExtension : ToolTabExtension
{
    public static readonly ObjectPropertyTabExtension Instance = new();

    public override bool CanMultiple => false;

    public override string Name => "Property editor";

    public override string DisplayName => "Property editor";

    public override ResourceReference<string>? Header => "S.Common.Properties";

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out IControl? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new ObjectPropertyEditor();
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
            context = new ObjectPropertyEditorViewModel(editViewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
