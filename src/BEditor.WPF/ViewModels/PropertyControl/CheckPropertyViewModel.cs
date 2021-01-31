
using System;
using System.Reactive.Linq;

using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Views.PropertyControls;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public class CheckPropertyViewModel
    {
        public CheckPropertyViewModel(CheckProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty();

            Command.Subscribe(x => Property.ChangeIsChecked(x).Execute());
            Reset.Subscribe(() => Property.ChangeIsChecked(Property.PropertyMetadata?.DefaultIsChecked ?? default).Execute());
            Bind.Subscribe(() =>
            {
                var window = new BindSettings(new BindSettingsViewModel<bool>(Property));
                window.ShowDialog();
            });
        }

        public ReadOnlyReactiveProperty<CheckPropertyMetadata?> Metadata { get; }
        public CheckProperty Property { get; }
        public ReactiveCommand<bool> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();
    }
}
