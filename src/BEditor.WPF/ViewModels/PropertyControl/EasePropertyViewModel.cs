using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Core.Command;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using BEditor.Core.Data.Property.Easing;

namespace BEditor.ViewModels.PropertyControl
{
    public class EasePropertyViewModel
    {
        public EasePropertyViewModel(EaseProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty();
            EasingChangeCommand.Subscribe(x => Property.ChangeEase(x).Execute());
        }

        public ReadOnlyReactiveProperty<EasePropertyMetadata?> Metadata { get; }
        public EaseProperty Property { get; }
        public ReactiveCommand<EasingMetadata> EasingChangeCommand { get; } = new();
    }
}
