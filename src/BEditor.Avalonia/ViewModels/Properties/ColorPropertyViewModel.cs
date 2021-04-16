using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia.Media;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Bindings;
using BEditor.Data.Property;
using BEditor.Views;
using BEditor.Views.Properties;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.Properties
{
    public sealed class ColorPropertyViewModel : IDisposable
    {
        private readonly CompositeDisposable _disposables = new();

        public ColorPropertyViewModel(ColorProperty property)
        {
            Property = property;
            var color = property.Value;

            Brush = property.ObserveProperty(p => p.Value)
                .Select(c => new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B)))
                .ToReactiveProperty()
                .AddTo(_disposables)!;

            Brush.Value = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));

            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactivePropertySlim()
                .AddTo(_disposables);

            Command.Subscribe(x => Property.ChangeColor(Drawing.Color.FromARGB(x.Item4, x.Item1, x.Item2, x.Item3)).Execute()).AddTo(_disposables);
            Reset.Subscribe(() => Property.ChangeColor(Property.PropertyMetadata?.DefaultColor ?? default).Execute()).AddTo(_disposables);
            Bind.Subscribe(async () =>
            {
                var window = new SetBinding
                {
                    DataContext = new SetBindingViewModel<Drawing.Color>(Property)
                };
                await window.ShowDialog(App.GetMainWindow());
            }).AddTo(_disposables);
            OpenDialog.Subscribe(async () => await Dialog.ShowDialog(App.GetMainWindow())).AddTo(_disposables);
        }
        ~ColorPropertyViewModel()
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
        //public static ObservableCollection<ColorList> ColorList { get; } = new();
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
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}