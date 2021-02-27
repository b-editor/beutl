using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Property;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public sealed class SelectorPropertyViewModel<T> : IDisposable
    {
        private readonly CompositeDisposable disposables = new();

        public SelectorPropertyViewModel(SelectorProperty<T> selector)
        {
            Property = selector;
            Metadata = selector.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty()
                .AddTo(disposables);

            Command.Subscribe(index => Property.ChangeSelect(index).Execute()).AddTo(disposables);
            Reset.Subscribe(() => Property.ChangeSelect(Property.PropertyMetadata!.DefaultItem).Execute()).AddTo(disposables);
        }
        ~SelectorPropertyViewModel()
        {
            Dispose();
        }

        public ReadOnlyReactiveProperty<SelectorPropertyMetadata<T?>?> Metadata { get; }
        public SelectorProperty<T> Property { get; }
        public ReactiveCommand<T> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();

        public void Dispose()
        {
            disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
    public sealed class SelectorPropertyViewModel : IDisposable
    {
        private readonly CompositeDisposable disposables = new();

        public SelectorPropertyViewModel(SelectorProperty selector)
        {
            Property = selector;
            Metadata = selector.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty()
                .AddTo(disposables);

            Command.Subscribe(index => Property.ChangeSelect(index).Execute()).AddTo(disposables);
            Reset.Subscribe(() => Property.ChangeSelect(Property.PropertyMetadata?.DefaultIndex ?? 0).Execute()).AddTo(disposables);
        }
        ~SelectorPropertyViewModel()
        {
            Dispose();
        }

        public ReadOnlyReactiveProperty<SelectorPropertyMetadata?> Metadata { get; }
        public SelectorProperty Property { get; }
        public ReactiveCommand<int> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();

        public void Dispose()
        {
            disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
