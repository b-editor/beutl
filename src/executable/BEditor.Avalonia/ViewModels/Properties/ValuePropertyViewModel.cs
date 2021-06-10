using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;

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

            Reset.Where(_ => Property.Value != (Property.PropertyMetadata?.DefaultValue ?? 0))
            .Subscribe(_ => Property.ChangeValue(Property.PropertyMetadata?.DefaultValue ?? 0).Execute())
            .AddTo(_disposables);

            Bind.Subscribe(async () =>
            {
                var window = new SetBinding
                {
                    DataContext = new SetBindingViewModel<float>(Property)
                };
                await window.ShowDialog(App.GetMainWindow());
            }).AddTo(_disposables);

            CopyID.Subscribe(async () => await Application.Current.Clipboard.SetTextAsync(Property.Id.ToString())).AddTo(_disposables);

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

        public ValueProperty Property { get; }

        public ReactiveCommand Reset { get; } = new();

        public ReactiveCommand Bind { get; } = new();

        public ReactiveCommand CopyID { get; } = new();

        public float Maximum { get; } = float.MaxValue;

        public float Minimum { get; } = float.MinValue;

        public void Dispose()
        {
            Reset.Dispose();
            Bind.Dispose();
            CopyID.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}