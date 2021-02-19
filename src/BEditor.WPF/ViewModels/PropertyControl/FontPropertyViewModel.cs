using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Drawing;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public class FontPropertyViewModel
    {
        public FontPropertyViewModel(FontProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty();

            Command.Subscribe(font => Property.ChangeFont(font).Execute());
            Reset.Subscribe(() => Property.ChangeFont(Property.PropertyMetadata?.SelectItem ?? FontProperty.FontList[0]).Execute());
        }

        public ReadOnlyReactiveProperty<FontPropertyMetadata?> Metadata { get; }
        public FontProperty Property { get; }
        public ReactiveCommand<Font> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
    }
}
