using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Views;
using BEditor.Views.PropertyControls;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public class FolderPropertyViewModel
    {
        public FolderPropertyViewModel(FolderProperty property)
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
                var file = OpenDialog();

                if (file != null)
                {
                    Property.ChangeFolder(file).Execute();
                }
            });
            Reset.Subscribe(() => Property.ChangeFolder(Property.PropertyMetadata?.Default ?? "").Execute());
            Bind.Subscribe(() =>
            {
                var window = new BindSettings(new BindSettingsViewModel<string>(Property));
                window.ShowDialog();
            });
        }

        public ReadOnlyReactiveProperty<FolderPropertyMetadata?> Metadata { get; }
        public FolderProperty Property { get; }
        public ReactiveCommand Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();
        public ReactiveProperty<int> PathMode { get; }

        private static string? OpenDialog()
        {
            // ダイアログのインスタンスを生成
            var dialog = new OpenFolderDialog();


            // ダイアログを表示する
            if (dialog.ShowDialog())
            {
                return dialog.FileName;
            }

            return null;
        }
    }
}
