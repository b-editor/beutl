using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Audio.Effects;
using Beutl.Editor.Components.EqualizerProperties.ViewModels;
using Beutl.Editor.Components.EqualizerProperties.Views;
using Beutl.PropertyAdapters;

namespace Beutl.Editor.Components.EqualizerProperties;

[PrimitiveImpl]
public sealed class EqualizerPropertiesExtension : PropertyEditorExtension
{
    public static new readonly EqualizerPropertiesExtension Instance = new();

    public override IEnumerable<IPropertyAdapter> MatchProperty(IReadOnlyList<IPropertyAdapter> properties)
    {
        // ImplementedTypeで判別してしまうとListItemAccessorImplが来たときに例外が発生してしまう
        var eqProps = properties.Where(p => p.GetEngineProperty()?.GetOwnerObject() is EqualizerEffect).ToArray();
        return eqProps.Length > 1 ? eqProps : [];
    }

    public override bool TryCreateContext(IReadOnlyList<IPropertyAdapter> properties,
        [NotNullWhen(true)] out IPropertyEditorContext? context)
    {
        if (properties.Count <= 1 || properties[0].ImplementedType != typeof(EqualizerEffect))
        {
            context = null;
            return false;
        }

        context = new EqualizerPropertiesViewModel(properties);
        return true;
    }

    public override bool TryCreateControl(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control)
    {
        control = new EqualizerPropertiesEditor();
        return true;
    }
}
