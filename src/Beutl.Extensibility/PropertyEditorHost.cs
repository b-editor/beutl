using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;

namespace Beutl.Extensibility;

// The editor host in Beutl.Editor.Components, which chooses editors and makes their controls.
internal interface IPropertyEditorHost
{
    bool TryCreateControl(IPropertyEditorContext context, PropertyEditorSurface surface, [NotNullWhen(true)] out Control? control);

    bool TryCreateDefault(IPropertyAdapter property, [NotNullWhen(true)] out IPropertyEditorContext? context);
}

internal static class PropertyEditorHost
{
    public static IPropertyEditorHost? Current { get; set; }

    public static bool TryCreateControl(
        IPropertyEditorContext context, PropertyEditorSurface surface, [NotNullWhen(true)] out Control? control)
    {
        if (Current is { } host)
            return host.TryCreateControl(context, surface, out control);

        control = null;
        return false;
    }
}
