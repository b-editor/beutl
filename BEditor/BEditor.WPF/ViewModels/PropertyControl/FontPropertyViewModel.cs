using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Core.Media;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Command;
using Reactive.Bindings;

namespace BEditor.ViewModels.PropertyControl
{
    public class FontPropertyViewModel
    {
        public FontPropertyViewModel(FontProperty property)
        {
            Property = property;
            Command.Subscribe(x => CommandManager.Do(new FontProperty.ChangeSelectCommand(property, (FontRecord)x.Item2)));
            Reset.Subscribe(() => CommandManager.Do(new FontProperty.ChangeSelectCommand(Property, Property.PropertyMetadata.SelectItem)));
        }

        public FontProperty Property { get; }
        public ReactiveCommand<(object, object)> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
    }
}
