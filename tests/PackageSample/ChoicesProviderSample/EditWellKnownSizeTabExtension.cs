using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Extensibility;

namespace PackageSample;

[Export]
public sealed class EditWellKnownSizeTabExtension : ToolTabExtension
{
    public override string Name => "Edit Well known size";

    public override string DisplayName => "Edit Well known size";

    public override string? Header => "Edit Well known size";

    public override bool CanMultiple => false;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new EditWellKnownSizeTab();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new EditWellKnownSizeTabViewModel(this);
        return true;
    }
}
