using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Models;
using BEditor.Models.Extension;
using BEditor.Views.PropertyControls;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public class TextPropertyViewModel
    {
        private string oldvalue;

        public TextPropertyViewModel(TextProperty property)
        {
            Property = property;
            oldvalue = Property.Value;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty();

            Reset.Subscribe(() => Property.ChangeText(Property.PropertyMetadata?.DefaultText ?? "").Execute());
            Bind.Subscribe(() =>
            {
                var window = new BindSettings(new BindSettingsViewModel<string>(Property));
                window.ShowDialog();
            });
            GotFocus.Subscribe(_ => oldvalue = Property.Value);
            LostFocus.Subscribe(text =>
            {
                Property.Value = oldvalue;

                Property.ChangeText(text).Execute();
            });
            TextChanged.Subscribe(text =>
            {
                Property.Value = text;

                AppData.Current.Project!.PreviewUpdate(Property.GetParent2()!);
            });
        }

        public ReadOnlyReactiveProperty<TextPropertyMetadata?> Metadata { get; }
        public TextProperty Property { get; }
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();
        public ReactiveCommand<string> GotFocus { get; } = new();
        public ReactiveCommand<string> LostFocus { get; } = new();
        public ReactiveCommand<string> TextChanged { get; } = new();
    }
}
