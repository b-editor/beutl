using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;

using BEditor.Command;
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
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactivePropertySlim()
                .AddTo(_disposables);
            EasingChangeCommand.Subscribe(x => Property.ChangeEase(x).Execute()).AddTo(_disposables);
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
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}