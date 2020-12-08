
using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public class ValuePropertyViewModel
    {
        public ValuePropertyViewModel(ValueProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty();

            Reset.Subscribe(() => CommandManager.Do(new ValueProperty.ChangeValueCommand(Property, Property.PropertyMetadata.DefaultValue)));
        }

        public ReadOnlyReactiveProperty<ValuePropertyMetadata> Metadata { get; }
        public ValueProperty Property { get; }
        public ReactiveCommand Reset { get; }
    }
}
