using System;
using System.Reactive.Disposables;

using BEditor.Data.Property;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.Properties
{
    public sealed class ButtonComponentViewModel : IDisposable
    {
        private readonly CompositeDisposable _disposables = new();

        public ButtonComponentViewModel(ButtonComponent button)
        {
            Property = button;

            Command.Subscribe(Property.Execute).AddTo(_disposables);
        }

        ~ButtonComponentViewModel()
        {
            Dispose();
        }

        public ButtonComponent Property { get; }

        public ReactiveCommand Command { get; } = new();

        public void Dispose()
        {
            Command.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}