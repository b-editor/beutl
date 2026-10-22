using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Beutl.Api.Services;

namespace Beutl.Editor.Components.PropertyEditors.Services;

// The extensions that still override PropertyEditorExtension's old virtual methods instead of registering editors.
internal static class LegacyPropertyEditors
{
    private static readonly ConditionalWeakTable<IPropertyEditorContext, PropertyEditorExtension> s_contexts = [];

    public static bool TryTake(
        PropertyEditorExtension extension,
        IPropertyAdapter[] remaining,
        PropertyEditorSurface surface,
        [NotNullWhen(true)] out IPropertyAdapter[]? taken,
        out IPropertyEditorContext? context)
    {
        taken = null;
        context = null;
        if (!IsLegacy(extension)) return false;

        IPropertyAdapter[] matched = ExtensionGuard.Call(extension, () => extension.MatchProperty(remaining).ToArray()) ?? [];
        if (matched.Length == 0) return false;

        taken = matched;
        context = ExtensionGuard.Call(extension, () =>
        {
            IPropertyEditorContext? created;
            bool result = surface switch
            {
                PropertyEditorSurface.Node => extension.TryCreateContextForNode(matched, out created),
                PropertyEditorSurface.ListItem => extension.TryCreateContextForListItem(matched[0], out created),
                PropertyEditorSurface.Settings => extension.TryCreateContextForSettings(matched, out created),
                _ => extension.TryCreateContext(matched, out created),
            };
            return result ? created : null;
        });
        if (context != null)
            s_contexts.AddOrUpdate(context, extension);
        return true;
    }

    // False for a context these extensions did not make, or whose extension made no control.
    public static bool TryCreateControl(
        IPropertyEditorContext context, PropertyEditorSurface surface, [NotNullWhen(true)] out Control? control)
    {
        control = null;
        if (!s_contexts.TryGetValue(context, out PropertyEditorExtension? extension)) return false;

        control = ExtensionGuard.Call(extension, () =>
        {
            Control? created = null;
            switch (surface)
            {
                case PropertyEditorSurface.Node:
                    extension.TryCreateControlForNode(context, out created);
                    break;
                case PropertyEditorSurface.ListItem:
                    extension.TryCreateControlForListItem(context, out IListItemEditor? item);
                    created = item as Control;
                    break;
                case PropertyEditorSurface.Settings:
                    extension.TryCreateControlForSettings(context, out created);
                    break;
                default:
                    extension.TryCreateControl(context, out created);
                    break;
            }

            return created;
        });
        return control != null;
    }

    private static bool IsLegacy(PropertyEditorExtension extension)
        => extension.GetType().GetMethod(nameof(PropertyEditorExtension.MatchProperty))?.DeclaringType != typeof(PropertyEditorExtension);
}
