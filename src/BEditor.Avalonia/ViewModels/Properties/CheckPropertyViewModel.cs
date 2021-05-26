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
    public sealed class CheckPropertyViewModel : IDisposable
    {
        private readonly CompositeDisposable _disposables = new();

        public CheckPropertyViewModel(CheckProperty property)
        {
            Property = property;

            Command
                .Where(i => i != Property.Value)
                .Subscribe(x => Property.ChangeIsChecked(x).Execute())
                .AddTo(_disposables);

            Reset
                .Where(_ => Property.Value != (Property.PropertyMetadata?.DefaultIsChecked ?? default))
                .Subscribe(_ => Property.ChangeIsChecked(Property.PropertyMetadata?.DefaultIsChecked ?? default).Execute())
                .AddTo(_disposables);

            Bind.Subscribe(async () =>
            {
                var window = new SetBinding
                {
                    DataContext = new SetBindingViewModel<bool>(Property)
                };
                await window.ShowDialog(App.GetMainWindow());
            }).AddTo(_disposables);

            CopyID.Subscribe(async () => await Application.Current.Clipboard.SetTextAsync(Property.Id.ToString())).AddTo(_disposables);
        }

        ~CheckPropertyViewModel()
        {
            Dispose();
        }

        public CheckProperty Property { get; }

        public ReactiveCommand<bool> Command { get; } = new();

        public ReactiveCommand Reset { get; } = new();

        public ReactiveCommand Bind { get; } = new();

        public ReactiveCommand CopyID { get; } = new();

        public void Dispose()
        {
            Command.Dispose();
            Reset.Dispose();
            Bind.Dispose();
            CopyID.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}