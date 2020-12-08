
using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;

using Reactive.Bindings;

namespace BEditor.ViewModels.PropertyControl
{
    public class ValuePropertyViewModel
    {
        public ValuePropertyViewModel(ValueProperty property)
        {
            Property = property;
            Reset.Subscribe(() => CommandManager.Do(new ValueProperty.ChangeValueCommand(Property, Property.PropertyMetadata.DefaultValue)));
        }

        public ValueProperty Property { get; }
        public ReactiveCommand Reset { get; }
    }
}
