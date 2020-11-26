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

namespace BEditor.ViewModels.PropertyControl
{
    public class EasePropertyViewModel
    {
        public EasePropertyViewModel(EaseProperty property)
        {
            Property = property;
            EasingChangeCommand.Subscribe(x => CommandManager.Do(new EaseProperty.ChangeEaseCommand(Property, x.Name)));
        }

        public EaseProperty Property { get; }
        public ReactiveCommand<EasingData> EasingChangeCommand { get; } = new();
    }
}
