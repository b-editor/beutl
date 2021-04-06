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

namespace BEditor.ViewModels.Properties
{
    public sealed class SelectorPropertyViewModel<T> : IDisposable where T : IJsonObject
    {
        private readonly CompositeDisposable _disposables = new();

        public SelectorPropertyViewModel(SelectorProperty<T> selector)
        {
            Property = selector;
            Metadata = selector.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactivePropertySlim()
                .AddTo(_disposables);

            Command.Subscribe(index => Property.ChangeSelect(index).Execute()).AddTo(_disposables);
            Reset.Subscribe(() => Property.ChangeSelect(Property.PropertyMetadata!.DefaultItem).Execute()).AddTo(_disposables);
        }
        ~SelectorPropertyViewModel()
        {
            Dispose();
        }

        public ReadOnlyReactivePropertySlim<SelectorPropertyMetadata<T>?> Metadata { get; }
        public SelectorProperty<T> Property { get; }
        public ReactiveCommand<T> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();

        public void Dispose()
        {
            Metadata.Dispose();
            Reset.Dispose();
            Bind.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
    public sealed class SelectorPropertyViewModel : IDisposable
    {
        private readonly CompositeDisposable _disposables = new();

        public SelectorPropertyViewModel(SelectorProperty selector)
        {
            Property = selector;
            Metadata = selector.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactivePropertySlim()
                .AddTo(_disposables);

            Command.Subscribe(index => Property.ChangeSelect(index).Execute()).AddTo(_disposables);
            Reset.Subscribe(() => Property.ChangeSelect(Property.PropertyMetadata?.DefaultIndex ?? 0).Execute()).AddTo(_disposables);
        }
        ~SelectorPropertyViewModel()
        {
            Dispose();
        }

        public ReadOnlyReactivePropertySlim<SelectorPropertyMetadata?> Metadata { get; }
        public SelectorProperty Property { get; }
        public ReactiveCommand<int> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();

        public void Dispose()
        {
            Metadata.Dispose();
            Reset.Dispose();
            Bind.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
