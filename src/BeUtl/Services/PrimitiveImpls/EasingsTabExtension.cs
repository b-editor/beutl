using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Framework;
using Beutl.ViewModels;
using Beutl.Views;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class EasingsTabExtension : ToolTabExtension
{
    public static readonly EasingsTabExtension Instance = new();

    public override bool CanMultiple => false;

    public override string Name => "Easings";

    public override string DisplayName => "Easings";

    public override string? Header => Strings.Easings;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out IControl? control)
    {
        control = new Easings();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new EasingsViewModel();
        return true;
    }
}
