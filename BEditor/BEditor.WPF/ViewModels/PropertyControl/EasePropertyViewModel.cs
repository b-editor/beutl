using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.EasingProperty;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Command;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public class EasePropertyViewModel
    {
        public EasePropertyViewModel(EaseProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty();
            EasingChangeCommand.Subscribe(x => CommandManager.Do(new EaseProperty.ChangeEaseCommand(Property, x.Name)));
        }

        public ReadOnlyReactiveProperty<EasePropertyMetadata> Metadata { get; }
        public EaseProperty Property { get; }
        public ReactiveCommand<EasingData> EasingChangeCommand { get; } = new();
    }
}
