
using System;
using System.Reactive.Linq;

using BEditor.Core.Command;
using BEditor.Core.Data.Control;
using BEditor.Core.Data.Primitive.Properties;
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

            Command.Subscribe(x => CommandManager.Do(new CheckProperty.ChangeCheckedCommand(Property, x)));
            Reset.Subscribe(() => CommandManager.Do(new CheckProperty.ChangeCheckedCommand(Property, Property.PropertyMetadata.DefaultIsChecked)));
            Bind.Subscribe(() =>
            {
                var window = new BindSettings()
                {
                    DataContext = new BindSettingsViewModel<bool>(Property)
                };
                window.ShowDialog();
            });
        }

        public ReadOnlyReactiveProperty<CheckPropertyMetadata> Metadata { get; }
        public CheckProperty Property { get; }
        public ReactiveCommand<bool> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();
    }
}
