using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Data.Property.Easing;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.Properties
{
    public sealed class EasePropertyViewModel : IDisposable
    {
        private readonly CompositeDisposable _disposables = new();

        public EasePropertyViewModel(EaseProperty property)
        {
            Property = property;

            EasingChangeCommand
                .Where(x => Property.EasingData != x)
                .Subscribe(x => Property.ChangeEase(x).Execute())
                .AddTo(_disposables);
        }

        ~EasePropertyViewModel()
        {
            Dispose();
        }

        public EaseProperty Property { get; }

        public ReactiveCommand<EasingMetadata> EasingChangeCommand { get; } = new();

        public void Dispose()
        {
            EasingChangeCommand.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}