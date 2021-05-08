using System;
using System.Reactive.Disposables;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Data.Property.Easing;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public sealed class EasePropertyViewModel : IDisposable
    {
        private readonly CompositeDisposable disposables = new();

        public EasePropertyViewModel(EaseProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactivePropertySlim()
                .AddTo(disposables);
            EasingChangeCommand.Subscribe(x => Property.ChangeEase(x).Execute()).AddTo(disposables);
        }
        ~EasePropertyViewModel()
        {
            Dispose();
        }

        public ReadOnlyReactivePropertySlim<EasePropertyMetadata?> Metadata { get; }
        public EaseProperty Property { get; }
        public ReactiveCommand<EasingMetadata> EasingChangeCommand { get; } = new();

        public void Dispose()
        {
            Metadata.Dispose();
            EasingChangeCommand.Dispose();
            disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}