using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Views.Properties;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.Properties
{
    public sealed class FontPropertyViewModel : IDisposable
    {
        private readonly CompositeDisposable _disposables = new();

        public FontPropertyViewModel(FontProperty property)
        {
            Property = property;

            Command.Where(i => Property.Value != i)
                .Subscribe(font => Property.ChangeFont(font).Execute())
                .AddTo(_disposables);

            Reset.Where(_ => Property.Value != (Property.PropertyMetadata?.SelectItem ?? FontManager.Default.LoadedFonts.First()))
                .Subscribe(_ => Property.ChangeFont(Property.PropertyMetadata?.SelectItem ?? FontManager.Default.LoadedFonts.First()).Execute())
                .AddTo(_disposables);

            Bind.Subscribe(async () =>
            {
                var window = new SetBinding
                {
                    DataContext = new SetBindingViewModel<Font>(Property)
                };
                await window.ShowDialog(App.GetMainWindow());
            }).AddTo(_disposables);

            CopyID.Subscribe(async () => await Application.Current.Clipboard.SetTextAsync(Property.Id.ToString())).AddTo(_disposables);
        }

        ~FontPropertyViewModel()
        {
            Dispose();
        }

        public FontProperty Property { get; }

        public ReactiveCommand<Font> Command { get; } = new();

        public ReactiveCommand Reset { get; } = new();

        public ReactiveCommand Bind { get; } = new();

        public ReactiveCommand CopyID { get; } = new();

        public static IEnumerable<string> Fonts { get; } = FontManager.Default.LoadedFonts.Select(f => f.Name).ToArray();

        public void Dispose()
        {
            Command.Dispose();
            Reset.Dispose();
            Bind.Dispose();
            CopyID.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}