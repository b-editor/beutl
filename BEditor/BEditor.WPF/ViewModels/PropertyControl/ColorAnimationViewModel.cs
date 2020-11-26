using System;
using System.Reactive.Linq;

using BEditor.Core.Command;
using BEditor.Core.Data.Control;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property.EasingProperty;

using Reactive.Bindings;

namespace BEditor.ViewModels.PropertyControl
{
    public class ColorAnimationViewModel
    {
        public ColorAnimationViewModel(ColorAnimationProperty property)
        {
            Property = property;
            EasingChangeCommand.Subscribe(x => CommandManager.Do(new ColorAnimationProperty.ChangeEaseCommand(Property, x.Name)));
        }

        public ColorAnimationProperty Property { get; }
        public ReactiveCommand<EasingData> EasingChangeCommand { get; } = new();
    }
}
