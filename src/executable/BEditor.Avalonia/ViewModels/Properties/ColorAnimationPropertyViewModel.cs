using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;

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

            EasingChangeCommand.Where(x => Property.EasingData != x)
                .Subscribe(x => Property.ChangeEase(x).Execute())
                .AddTo(_disposables);
        }

        ~ColorAnimationPropertyViewModel()
        {
            Dispose();
        }

        public ColorAnimationProperty Property { get; }

        public ReactiveCommand<EasingMetadata> EasingChangeCommand { get; } = new();

        public void Dispose()
        {
            EasingChangeCommand.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}