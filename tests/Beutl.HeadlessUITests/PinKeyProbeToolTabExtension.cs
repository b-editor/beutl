using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Extensibility;

// Deliberately outside any namespace that repeats the assembly name, so a pin key that names the
// assembly can be told apart from one that only carries the type name.
namespace PinKeyProbe;

internal sealed class PinKeyProbeToolTabExtension : ToolTabExtension
{
    public override bool CanMultiple => true;

    public override bool TryCreateContent(
        IEditorContext editorContext,
        [NotNullWhen(true)] out Control? control)
    {
        control = null;
        return false;
    }

    public override bool TryCreateContext(
        IEditorContext editorContext,
        [NotNullWhen(true)] out IToolContext? context)
    {
        context = null;
        return false;
    }
}
