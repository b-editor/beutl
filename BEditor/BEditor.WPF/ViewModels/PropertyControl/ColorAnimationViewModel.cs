using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property.EasingProperty;
using BEditor.ViewModels.Helper;

namespace BEditor.ViewModels.PropertyControl
{
    public class ColorAnimationViewModel
    {
        public ColorAnimationViewModel(ColorAnimationProperty property)
        {
            Property = property;
            EasingChangeCommand = new(x =>
            {
                CommandManager.Do(new ColorAnimationProperty.ChangeEaseCommand(Property, x.Name));
            });
        }

        public ColorAnimationProperty Property { get; }
        public DelegateCommand<EasingData> EasingChangeCommand { get; }
    }
}
