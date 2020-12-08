using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public class TextPropertyViewModel
    {
        public TextPropertyViewModel(TextProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty();

            Reset.Subscribe(() => CommandManager.Do(new TextProperty.ChangeTextCommand(Property, Property.PropertyMetadata.DefaultText)));
        }

        public ReadOnlyReactiveProperty<TextPropertyMetadata> Metadata { get; }
        public TextProperty Property { get; }
        public ReactiveCommand Reset { get; } = new();
    }
}
