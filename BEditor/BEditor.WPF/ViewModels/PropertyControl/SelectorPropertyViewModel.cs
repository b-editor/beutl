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
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public class SelectorPropertyViewModel
    {
        public SelectorPropertyViewModel(SelectorProperty selector)
        {
            Property = selector;
            Metadata = selector.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty();

            Command.Subscribe(index => CommandManager.Do(new SelectorProperty.ChangeSelectCommand(Property, index)));
            Reset.Subscribe(() => CommandManager.Do(new SelectorProperty.ChangeSelectCommand(Property, Property.PropertyMetadata.DefaultIndex)));
        }

        public ReadOnlyReactiveProperty<SelectorPropertyMetadata> Metadata { get; }
        public SelectorProperty Property { get; }
        public ReactiveCommand<int> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();
    }
}
