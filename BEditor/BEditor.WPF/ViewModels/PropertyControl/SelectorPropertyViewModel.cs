using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Command;
using Reactive.Bindings;

namespace BEditor.ViewModels.PropertyControl
{
    public class SelectorPropertyViewModel
    {
        public SelectorPropertyViewModel(SelectorProperty selector)
        {
            Property = selector;
            Command.Subscribe(x => CommandManager.Do(new SelectorProperty.ChangeSelectCommand(Property, (int)x.Item1)));
            Reset.Subscribe(() => CommandManager.Do(new SelectorProperty.ChangeSelectCommand(Property, Property.PropertyMetadata.DefaultIndex)));
        }

        public SelectorProperty Property { get; }
        public ReactiveCommand<(object, object)> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
    }
}
