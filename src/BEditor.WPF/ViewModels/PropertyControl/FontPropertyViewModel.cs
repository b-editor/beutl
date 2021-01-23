using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Core.Command;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using BEditor.Drawing;

namespace BEditor.ViewModels.PropertyControl
{
    public class FontPropertyViewModel
    {
        public FontPropertyViewModel(FontProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty();

            Command.Subscribe(font => CommandManager.Do(new FontProperty.ChangeSelectCommand(property, font)));
            Reset.Subscribe(() => CommandManager.Do(new FontProperty.ChangeSelectCommand(Property, Property.PropertyMetadata?.SelectItem ?? FontProperty.FontList[0])));
        }

        public ReadOnlyReactiveProperty<FontPropertyMetadata?> Metadata { get; }
        public FontProperty Property { get; }
        public ReactiveCommand<Font> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
    }
}
