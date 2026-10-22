using Beutl.Audio.Effects;
using Beutl.Editor.Components.EqualizerProperties.ViewModels;
using Beutl.Editor.Components.EqualizerProperties.Views;
using Beutl.PropertyAdapters;

namespace Beutl.Editor.Components.EqualizerProperties;

public sealed class EqualizerPropertiesExtension : PropertyEditorExtension
{
    public static new readonly EqualizerPropertiesExtension Instance = new();

    protected internal override void RegisterEditors(PropertyEditorRegistry registry)
    {
        // ImplementedTypeで判別してしまうとListItemAccessorImplが来たときに例外が発生してしまう
        registry.RegisterGroup(
            properties =>
            {
                IPropertyAdapter[] eqProps = [.. properties.Where(p => p.GetEngineProperty()?.GetOwnerObject() is EqualizerEffect)];
                return eqProps.Length > 1 ? eqProps : [];
            },
            properties => new EqualizerPropertiesViewModel(properties),
            _ => new EqualizerPropertiesEditor());
    }
}
