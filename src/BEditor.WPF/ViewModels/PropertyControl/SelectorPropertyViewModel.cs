using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Property;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public class SelectorPropertyViewModel<T>
    {
        public SelectorPropertyViewModel(SelectorProperty<T> selector)
        {
            Property = selector;
            Metadata = selector.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty();

            Command.Subscribe(index => Property.ChangeSelect(index).Execute());
            Reset.Subscribe(() => Property.ChangeSelect(Property.PropertyMetadata!.DefaultItem).Execute());
        }

        public ReadOnlyReactiveProperty<SelectorPropertyMetadata<T?>?> Metadata { get; }
        public SelectorProperty<T> Property { get; }
        public ReactiveCommand<T> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();
    }
    public class SelectorPropertyViewModel
    {
        public SelectorPropertyViewModel(SelectorProperty selector)
        {
            Property = selector;
            Metadata = selector.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty();

            Command.Subscribe(index => Property.ChangeSelect(index).Execute());
            Reset.Subscribe(() => Property.ChangeSelect(Property.PropertyMetadata?.DefaultIndex ?? 0).Execute());
        }

        public ReadOnlyReactiveProperty<SelectorPropertyMetadata?> Metadata { get; }
        public SelectorProperty Property { get; }
        public ReactiveCommand<int> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();
    }
}
