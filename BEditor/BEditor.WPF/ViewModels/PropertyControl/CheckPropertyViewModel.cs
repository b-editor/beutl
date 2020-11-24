
using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.ViewModels.Helper;

namespace BEditor.ViewModels.PropertyControl
{
    public class CheckPropertyViewModel
    {
        public CheckPropertyViewModel(CheckProperty property)
        {
            Property = property;
            Command = new DelegateCommand<bool>(x =>
            {
                CommandManager.Do(new CheckProperty.ChangeCheckedCommand(Property, x));
            });
            Reset = new(() =>
            {
                CommandManager.Do(new CheckProperty.ChangeCheckedCommand(Property, Property.PropertyMetadata.DefaultIsChecked));
            });
        }

        public CheckProperty Property { get; }
        public DelegateCommand<bool> Command { get; }
        public DelegateCommand Reset { get; }
    }
}
