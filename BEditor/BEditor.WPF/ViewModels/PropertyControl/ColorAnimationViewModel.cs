using System;
using System.Reactive.Linq;

using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.Easing;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public class ColorAnimationViewModel
    {
        public ColorAnimationViewModel(ColorAnimationProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty();

            EasingChangeCommand.Subscribe(x => CommandManager.Do(new ColorAnimationProperty.ChangeEaseCommand(Property, x.Name)));
        }

        public ReadOnlyReactiveProperty<ColorAnimationPropertyMetadata> Metadata { get; }
        public ColorAnimationProperty Property { get; }
        public ReactiveCommand<EasingMetadata> EasingChangeCommand { get; } = new();
    }
}
