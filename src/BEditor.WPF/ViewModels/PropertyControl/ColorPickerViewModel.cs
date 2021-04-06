using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Disposables;
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
    public sealed class ColorPickerViewModel : IDisposable
    {
        private readonly CompositeDisposable disposables = new();

        public ColorPickerViewModel(ColorProperty property)
        {
            Property = property;
            var color = property.Value;

            Brush = property.ObserveProperty(p => p.Value)
                .Select(c => new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B)))
                .ToReactiveProperty()
                .AddTo(disposables)!;

            Brush.Value = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));

            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactivePropertySlim()
                .AddTo(disposables);

            Command.Subscribe(x => Property.ChangeColor(Drawing.Color.FromARGB(x.Item4, x.Item1, x.Item2, x.Item3)).Execute()).AddTo(disposables);
            Reset.Subscribe(() => Property.ChangeColor(Property.PropertyMetadata?.DefaultColor ?? default).Execute()).AddTo(disposables);
            Bind.Subscribe(() =>
            {
                var window = new BindSettings(new BindSettingsViewModel<Drawing.Color>(Property));
                window.ShowDialog();
            }).AddTo(disposables);
            OpenDialog.Subscribe(() => Dialog.ShowDialog()).AddTo(disposables);
        }
        ~ColorPickerViewModel()
        {
            Dispose();
        }

        private ColorDialog Dialog
        {
            get
            {
                var d = new ColorDialog(this);

                d.col.Red = Property.Value.R;
                d.col.Green = Property.Value.G;
                d.col.Blue = Property.Value.B;
                d.col.Alpha = Property.Value.A;

                return d;
            }
        }
        public static ObservableCollection<ColorList> ColorList { get; } = new();
        public ReadOnlyReactivePropertySlim<ColorPropertyMetadata?> Metadata { get; }
        public ColorProperty Property { get; }
        public ReactiveCommand<(byte, byte, byte, byte)> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();
        public ReactiveCommand OpenDialog { get; } = new();
        public ReactiveProperty<SolidColorBrush> Brush { get; }

        public void Dispose()
        {
            Metadata.Dispose();
            Command.Dispose();
            Reset.Dispose();
            Bind.Dispose();
            OpenDialog.Dispose();
            Brush.Dispose();
            disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
