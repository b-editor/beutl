using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Data.Property.Easing;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public sealed class ColorAnimationViewModel : IDisposable
    {
        private readonly CompositeDisposable disposables = new();

        public ColorAnimationViewModel(ColorAnimationProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactivePropertySlim()
                .AddTo(disposables);

            EasingChangeCommand.Subscribe(x => Property.ChangeEase(x).Execute()).AddTo(disposables);
        }
        ~ColorAnimationViewModel()
        {
            Dispose();
        }

        public ReadOnlyReactivePropertySlim<ColorAnimationPropertyMetadata?> Metadata { get; }
        public ColorAnimationProperty Property { get; }
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