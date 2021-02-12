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
using BEditor.Core.Service;
using Microsoft.Win32;
using System.Reactive.Linq;

namespace BEditor.ViewModels.PropertyControl
{
    public class FilePropertyViewModel
    {
        public FilePropertyViewModel(FileProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty();

            PathMode = property.ObserveProperty(p => p.Mode)
                .Select(i => (int)i)
                .ToReactiveProperty();

            PathMode.Subscribe(i => Property.Mode = (FilePathType)i);

            Command.Subscribe(x =>
            {
                var file = OpenDialog(Property.PropertyMetadata?.Filter ?? null);

                if (file != null)
                {
                    Property.ChangeFile(file).Execute();
                }
            });
            Reset.Subscribe(() => Property.ChangeFile(Property.PropertyMetadata?.DefaultFile ?? "").Execute());
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
        public ReactiveProperty<int> PathMode { get; }

        private static string? OpenDialog(FileFilter? filter)
        {
            if (Services.FileDialogService is null) return null;
            var dialog = Services.FileDialogService;
            var record = new OpenFileRecord();

            if (filter is not null)
            {
                record.Filters.Add(filter);
            }

            // ダイアログを表示する
            if (dialog.ShowOpenFileDialog(record))
            {
                return record.FileName;
            }

            return null;
        }
    }
}
