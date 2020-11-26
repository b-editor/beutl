
using System;
using System.Reactive.Linq;

using BEditor.Core.Command;
using BEditor.Core.Data.Control;
using BEditor.Core.Data.Primitive.Properties;

using Reactive.Bindings;

namespace BEditor.ViewModels.PropertyControl
{
    public class CheckPropertyViewModel
    {
        public CheckPropertyViewModel(CheckProperty property)
        {
            Property = property;

            Command.Subscribe(x => CommandManager.Do(new CheckProperty.ChangeCheckedCommand(Property, x)));
            Reset.Subscribe(() => CommandManager.Do(new CheckProperty.ChangeCheckedCommand(Property, Property.PropertyMetadata.DefaultIsChecked)));
        }

        public CheckProperty Property { get; }
        public ReactiveCommand<bool> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
    }
}
