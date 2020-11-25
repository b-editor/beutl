
using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.ViewModels.Helper;

namespace BEditor.ViewModels.PropertyControl
{
    public class ValuePropertyViewModel
    {
        public ValuePropertyViewModel(ValueProperty property)
        {
            Property = property;
            Reset = new(() => CommandManager.Do(new ValueProperty.ChangeValueCommand(Property, Property.PropertyMetadata.DefaultValue)));
        }

        public ValueProperty Property { get; }
        public DelegateCommand Reset { get; }
    }
}
