using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using BeUtl.Framework;
using BeUtl.ViewModels;
using BeUtl.ViewModels.Tools;
using BeUtl.Views.Tools;

namespace BeUtl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class StyleEditorTabExtension : ToolTabExtension
{
    public static readonly StyleEditorTabExtension Instance = new();

    public override bool CanMultiple => false;

    public override string Name => "Style editor";

    public override string DisplayName => "Style editor";

    public override IObservable<string>? Header => S.Common.StyleObservable;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out IControl? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new StyleEditor();
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
            context = new StyleEditorViewModel(viewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
