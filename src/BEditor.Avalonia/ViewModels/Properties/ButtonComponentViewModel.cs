using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

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
            Metadata = button.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactivePropertySlim()
                .AddTo(_disposables);

            Command.Subscribe(Property.Execute).AddTo(_disposables);
        }
        ~ButtonComponentViewModel()
        {
            Dispose();
        }

        public ReadOnlyReactivePropertySlim<ButtonComponentMetadata?> Metadata { get; }
        public ButtonComponent Property { get; }
        public ReactiveCommand Command { get; } = new();

        public void Dispose()
        {
            Metadata.Dispose();
            Command.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
