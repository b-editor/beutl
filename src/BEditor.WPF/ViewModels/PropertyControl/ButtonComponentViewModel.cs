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

namespace BEditor.ViewModels.PropertyControl
{
    public sealed class ButtonComponentViewModel : IDisposable
    {
        private readonly CompositeDisposable disposables = new();

        public ButtonComponentViewModel(ButtonComponent button)
        {
            Property = button;
            Metadata = button.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactivePropertySlim()
                .AddTo(disposables);

            Command.Subscribe(() => Property.Execute()).AddTo(disposables);
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
            disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
