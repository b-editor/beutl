using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;
using Avalonia.Media;

using BEditor.Data;
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

            Command
                .Select(x => Drawing.Color.FromArgb(x.Item4, x.Item1, x.Item2, x.Item3))
                .Where(x => x != Property.Value)
                .Subscribe(x => Property.ChangeColor(x).Execute())
                .AddTo(_disposables);

            Reset
                .Where(_ => Property.Value != (Property.PropertyMetadata?.DefaultColor ?? default))
                .Subscribe(_ => Property.ChangeColor(Property.PropertyMetadata?.DefaultColor ?? default).Execute())
                .AddTo(_disposables);

            Bind.Subscribe(async () =>
            {
                var window = new SetBinding
                {
                    DataContext = new SetBindingViewModel<Drawing.Color>(Property)
                };
                await window.ShowDialog(App.GetMainWindow());
            }).AddTo(_disposables);

            OpenDialog.Subscribe(async () => await Dialog.ShowDialog(App.GetMainWindow())).AddTo(_disposables);

            CopyID.Subscribe(async () => await Application.Current.Clipboard.SetTextAsync(Property.Id.ToString())).AddTo(_disposables);
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

                d.col.Color = new Color(Property.Value.A, Property.Value.R, Property.Value.G, Property.Value.B);

                return d;
            }
        }

        public ColorProperty Property { get; }

        public ReactiveCommand<(byte, byte, byte, byte)> Command { get; } = new();

        public ReactiveCommand Reset { get; } = new();

        public ReactiveCommand Bind { get; } = new();

        public ReactiveCommand CopyID { get; } = new();

        public ReactiveCommand OpenDialog { get; } = new();

        public ReactiveProperty<SolidColorBrush> Brush { get; }

        public void Dispose()
        {
            Command.Dispose();
            Reset.Dispose();
            Bind.Dispose();
            CopyID.Dispose();
            OpenDialog.Dispose();
            Brush.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}