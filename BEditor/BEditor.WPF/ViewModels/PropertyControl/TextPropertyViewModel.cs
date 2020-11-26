using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;

using Reactive.Bindings;

namespace BEditor.ViewModels.PropertyControl
{
    public class TextPropertyViewModel
    {
        public TextPropertyViewModel(TextProperty property)
        {
            Property = property;
            Reset.Subscribe(() => CommandManager.Do(new TextProperty.ChangeTextCommand(Property, Property.PropertyMetadata.DefaultText)));
        }

        public TextProperty Property { get; }
        public ReactiveCommand Reset { get; } = new();
    }
}
