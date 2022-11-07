using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Framework;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class ObjectPropertyTabExtension : ToolTabExtension
{
    public static readonly ObjectPropertyTabExtension Instance = new();

    public override bool CanMultiple => false;

    public override string Name => "Property editor";

    public override string DisplayName => "Property editor";

    public override IObservable<string>? Header => Observable.Return(Strings.Properties);

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
