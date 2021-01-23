using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Core.Command;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using BEditor.Views.PropertyControls;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace BEditor.ViewModels.PropertyControl
{
    public class FilePropertyViewModel
    {
        public FilePropertyViewModel(FileProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty();

            Command.Subscribe(x =>
            {
                var file = OpenDialog(Property.PropertyMetadata?.FilterName ?? "", Property.PropertyMetadata?.Filter ?? "");

                if (file != null)
                {
                    CommandManager.Do(new FileProperty.ChangeFileCommand(Property, file));
                }
            });
            Reset.Subscribe(() => CommandManager.Do(new FileProperty.ChangeFileCommand(Property, Property.PropertyMetadata?.DefaultFile ?? "")));
            Bind.Subscribe(() =>
            {
                var window = new BindSettings(new BindSettingsViewModel<string>(property));
                window.ShowDialog();
            });
        }

        public ReadOnlyReactiveProperty<FilePropertyMetadata?> Metadata { get; }
        public FileProperty Property { get; }
        public ReactiveCommand<Func<string, string, string>> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();

        private static string? OpenDialog(string filtername, string filter)
        {
            var dialog = new CommonOpenFileDialog();


            dialog.Filters.Add(new CommonFileDialogFilter(filtername, filter));
            dialog.Filters.Add(new CommonFileDialogFilter(null, "*.*"));

            // ダイアログを表示する
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                return dialog.FileName;
            }

            return null;
        }
    }
}
