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

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public sealed class FontPropertyViewModel : IDisposable
    {
        private readonly CompositeDisposable disposables = new();

        public FontPropertyViewModel(FontProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty()
                .AddTo(disposables);

            Command.Subscribe(font => Property.ChangeFont(font).Execute()).AddTo(disposables);
            Reset.Subscribe(() => Property.ChangeFont(Property.PropertyMetadata?.SelectItem ?? FontManager.Default.LoadedFonts.First()).Execute()).AddTo(disposables);
        }
        ~FontPropertyViewModel()
        {
            Dispose();
        }

        public ReadOnlyReactiveProperty<FontPropertyMetadata?> Metadata { get; }
        public FontProperty Property { get; }
        public ReactiveCommand<Font> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public static IEnumerable<Font> Fonts => FontManager.Default.LoadedFonts;

        public void Dispose()
        {
            disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
