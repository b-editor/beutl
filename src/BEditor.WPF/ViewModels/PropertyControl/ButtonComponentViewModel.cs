using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data.Property;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public class ButtonComponentViewModel
    {
        public ButtonComponentViewModel(ButtonComponent button)
        {
            Property = button;
            Metadata = button.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty();

            Command.Subscribe(() => Property.Execute());
        }

        public ReadOnlyReactiveProperty<ButtonComponentMetadata?> Metadata { get; }
        public ButtonComponent Property { get; }
        public ReactiveCommand Command { get; } = new();
    }
}
