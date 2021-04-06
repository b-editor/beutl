using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using Reactive.Bindings;

namespace BEditor.ViewModels.Settings
{
    public class FontsViewModel
    {
        public FontsViewModel()
        {
            IsSelected = SelectFont.Select(dir => dir is not null).ToReadOnlyReactiveProperty();

            Remove.Subscribe(() =>
            {
                BEditor.Settings.Default.IncludeFontDir.Remove(SelectFont.Value);
            });
        }

        public ReactiveProperty<string> SelectFont { get; } = new();
        public ReadOnlyReactiveProperty<bool> IsSelected { get; }
        public ReactiveCommand Remove { get; } = new();
    }
}
