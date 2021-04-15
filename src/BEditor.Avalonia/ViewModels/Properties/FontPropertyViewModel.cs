using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;

using BEditor.Command;
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
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactivePropertySlim()
                .AddTo(_disposables);

            Command.Subscribe(font => Property.ChangeFont(font).Execute()).AddTo(_disposables);
            Reset.Subscribe(() => Property.ChangeFont(Property.PropertyMetadata?.SelectItem ?? FontManager.Default.LoadedFonts.First()).Execute()).AddTo(_disposables);
            Bind.Subscribe(async () =>
            {
                var window = new SetBinding
                {
                    DataContext = new SetBindingViewModel<Font>(Property)
                };
                await window.ShowDialog(App.GetMainWindow());
            }).AddTo(_disposables);
        }
        ~FontPropertyViewModel()
        {
            Dispose();
        }

        public ReadOnlyReactivePropertySlim<FontPropertyMetadata?> Metadata { get; }
        public FontProperty Property { get; }
        public ReactiveCommand<Font> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();
        public static IEnumerable<string> Fonts { get; } = FontManager.Default.LoadedFonts.Select(f => f.Name).ToArray();

        public void Dispose()
        {
            Metadata.Dispose();
            Command.Dispose();
            Reset.Dispose();
            Bind.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}