using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Property;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.Properties
{
    public sealed class CheckPropertyViewModel : IDisposable
    {
        private readonly CompositeDisposable _disposables = new();

        public CheckPropertyViewModel(CheckProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactivePropertySlim()
                .AddTo(_disposables);

            Command.Subscribe(x => Property.ChangeIsChecked(x).Execute()).AddTo(_disposables);
            Reset.Subscribe(() => Property.ChangeIsChecked(Property.PropertyMetadata?.DefaultIsChecked ?? default).Execute()).AddTo(_disposables);
        }
        ~CheckPropertyViewModel()
        {
            Dispose();
        }

        public ReadOnlyReactivePropertySlim<CheckPropertyMetadata?> Metadata { get; }
        public CheckProperty Property { get; }
        public ReactiveCommand<bool> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();

        public void Dispose()
        {
            Metadata.Dispose();
            Command.Dispose();
            Reset.Dispose();
            Bind.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
