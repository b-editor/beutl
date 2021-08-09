using System;
using System.Linq;
using System.Reactive.Linq;

using Reactive.Bindings;

namespace BEditor.ViewModels.Settings
{
    public sealed class FontsViewModel
    {
        public FontsViewModel()
        {
            IsSelected = SelectFont.Select(dir => dir is not null).ToReadOnlyReactiveProperty();

            Remove.Subscribe(str => BEditor.Settings.Default.IncludeFontDir.Remove(str));
        }

        public ReactiveProperty<string> SelectFont { get; } = new();

        public ReadOnlyReactiveProperty<bool> IsSelected { get; }

        public ReactiveCommand<string> Remove { get; } = new();
    }
}