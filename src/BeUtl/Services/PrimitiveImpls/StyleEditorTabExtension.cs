using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Framework;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class StyleEditorTabExtension : ToolTabExtension
{
    public static readonly StyleEditorTabExtension Instance = new();

    public override bool CanMultiple => false;

    public override string Name => "Style editor";

    public override string DisplayName => "Style editor";

    public override IObservable<string>? Header => Observable.Return(Strings.Style);

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
