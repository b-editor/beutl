using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Windows.Media;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Bindings;
using BEditor.Data.Property;
using BEditor.ViewModels.CustomControl;
using BEditor.Views;
using BEditor.Views.PropertyControls;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public class ColorPickerViewModel : BasePropertyChanged
    {
        private static readonly PropertyChangedEventArgs brushArgs = new(nameof(Brush));

        public ColorPickerViewModel(ColorProperty property)
        {
            Property = property;
            property.PropertyChanged += (s, e) => RaisePropertyChanged(brushArgs);
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty();

            Command.Subscribe(x => Property.ChangeColor(Drawing.Color.FromARGB(x.Item4, x.Item1, x.Item2, x.Item3)).Execute());
            Reset.Subscribe(() => Property.ChangeColor(Property.PropertyMetadata?.DefaultColor ?? default).Execute());
            Bind.Subscribe(() =>
            {
                var window = new BindSettings(new BindSettingsViewModel<Drawing.Color>(Property));
                window.ShowDialog();
            });
            OpenDialog.Subscribe(() => Dialog.ShowDialog());
        }

        private ColorDialog Dialog
        {
            get
            {
                var d = new ColorDialog(this);

                d.col.Red = Property.Color.R;
                d.col.Green = Property.Color.G;
                d.col.Blue = Property.Color.B;
                d.col.Alpha = Property.Color.A;

                return d;
            }
        }
        public static ObservableCollection<ColorList> ColorList { get; } = new();
        public ReadOnlyReactiveProperty<ColorPropertyMetadata?> Metadata { get; }
        public ColorProperty Property { get; }
        public ReactiveCommand<(byte, byte, byte, byte)> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();
        public ReactiveCommand OpenDialog { get; } = new();
        public SolidColorBrush Brush => new(Color.FromArgb(Property.Color.A, Property.Color.R, Property.Color.G, Property.Color.B));
    }
}
