using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Views.PropertyControls;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public sealed class CheckPropertyViewModel : IDisposable
    {
        private readonly CompositeDisposable disposables = new();

        public CheckPropertyViewModel(CheckProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty()
                .AddTo(disposables);

            Command.Subscribe(x => Property.ChangeIsChecked(x).Execute()).AddTo(disposables);
            Reset.Subscribe(() => Property.ChangeIsChecked(Property.PropertyMetadata?.DefaultIsChecked ?? default).Execute()).AddTo(disposables);
            Bind.Subscribe(() =>
            {
                var window = new BindSettings(new BindSettingsViewModel<bool>(Property));
                window.ShowDialog();
            }).AddTo(disposables);
        }
        ~CheckPropertyViewModel()
        {
            Dispose();
        }

        public ReadOnlyReactiveProperty<CheckPropertyMetadata?> Metadata { get; }
        public CheckProperty Property { get; }
        public ReactiveCommand<bool> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();

        public void Dispose()
        {
            disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
