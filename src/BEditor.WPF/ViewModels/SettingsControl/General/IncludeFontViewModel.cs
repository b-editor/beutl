using System.Linq;
using System.Reactive.Linq;

using BEditor.Views;

using Reactive.Bindings;

namespace BEditor.ViewModels.SettingsControl.General
{
    public class IncludeFontViewModel
    {
        public IncludeFontViewModel()
        {
            IsSelected = SelectFont.Select(dir => dir is not null).ToReadOnlyReactivePropertySlim();

            Add.Subscribe(() =>
            {
                var dialog = new OpenFolderDialog();

                if (dialog.ShowDialog())
                {
                    Settings.Default.IncludeFontDir.Add(dialog.FileName);
                }
            });
            Remove.Subscribe(() =>
            {
                Settings.Default.IncludeFontDir.Remove(SelectFont.Value);
            });
        }

        public ReactivePropertySlim<string> SelectFont { get; } = new();
        public ReadOnlyReactivePropertySlim<bool> IsSelected { get; }
        public ReactiveCommand Add { get; } = new();
        public ReactiveCommand Remove { get; } = new();
    }
}