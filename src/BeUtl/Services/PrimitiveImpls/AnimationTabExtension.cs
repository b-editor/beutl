using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using BeUtl.Framework;
using BeUtl.ViewModels;
using BeUtl.ViewModels.Editors;
using BeUtl.Views.Editors;

namespace BeUtl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class AnimationTabExtension : ToolTabExtension
{
    public static readonly AnimationTabExtension Instance = new();

    public override bool CanMultiple => false;

    public override string Name => "Animation";

    public override string DisplayName => "Animation";

    public override ResourceReference<string>? Header => "S.Common.Animation";

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out IControl? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new AnimationTab();
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
        if (editorContext is EditViewModel viewModel)
        {
            context = new AnimationTabViewModel();
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
