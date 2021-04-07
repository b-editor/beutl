using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Data.Property.Easing;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.Properties
{
    public sealed class ColorAnimationPropertyViewModel : IDisposable
    {
        private readonly CompositeDisposable _disposables = new();

        public ColorAnimationPropertyViewModel(ColorAnimationProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactivePropertySlim()
                .AddTo(_disposables);

            EasingChangeCommand.Subscribe(x => Property.ChangeEase(x).Execute()).AddTo(_disposables);
        }
        ~ColorAnimationPropertyViewModel()
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
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
