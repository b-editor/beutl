using System;
using System.Reactive.Disposables;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Views.Properties;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.Properties
{
    public sealed class ValuePropertyViewModel : IDisposable
    {
        private readonly CompositeDisposable _disposables = new();

        public ValuePropertyViewModel(ValueProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactivePropertySlim()
                .AddTo(_disposables);

            Reset.Subscribe(() => Property.ChangeValue(Property.PropertyMetadata?.DefaultValue ?? 0).Execute()).AddTo(_disposables);
            Bind.Subscribe(async () =>
            {
                var window = new SetBinding
                {
                    DataContext = new SetBindingViewModel<float>(Property)
                };
                await window.ShowDialog(App.GetMainWindow());
            }).AddTo(_disposables);

            if (property.PropertyMetadata is null) return;

            if (!float.IsNaN(property.PropertyMetadata.Max))
            {
                Maximum = property.PropertyMetadata.Max;
            }

            if (!float.IsNaN(property.PropertyMetadata.Min))
            {
                Minimum = property.PropertyMetadata.Min;
            }

        }
        ~ValuePropertyViewModel()
        {
            Dispose();
        }

        public ReadOnlyReactivePropertySlim<ValuePropertyMetadata?> Metadata { get; }
        public ValueProperty Property { get; }
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();
        public float Maximum { get; } = float.MaxValue;
        public float Minimum { get; } = float.MinValue;

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
