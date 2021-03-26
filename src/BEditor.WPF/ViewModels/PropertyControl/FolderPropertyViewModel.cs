using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Views;
using BEditor.Views.PropertyControls;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public sealed class FolderPropertyViewModel : IDisposable
    {
        private readonly CompositeDisposable disposables = new();

        public FolderPropertyViewModel(FolderProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactivePropertySlim()
                .AddTo(disposables);

            PathMode = property.ObserveProperty(p => p.Mode)
                .Select(i => (int)i)
                .ToReactiveProperty()
                .AddTo(disposables);

            PathMode.Subscribe(i => Property.Mode = (FilePathType)i).AddTo(disposables);

            Command.Subscribe(x =>
            {
                var file = OpenDialog();

                if (file != null)
                {
                    Property.ChangeFolder(file).Execute();
                }
            }).AddTo(disposables);
            Reset.Subscribe(() => Property.ChangeFolder(Property.PropertyMetadata?.Default ?? "").Execute()).AddTo(disposables);
            Bind.Subscribe(() =>
            {
                var window = new BindSettings(new BindSettingsViewModel<string>(Property));
                window.ShowDialog();
            }).AddTo(disposables);
        }
        ~FolderPropertyViewModel()
        {
            Dispose();
        }

        public ReadOnlyReactivePropertySlim<FolderPropertyMetadata?> Metadata { get; }
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

        public void Dispose()
        {
            Metadata.Dispose();
            Command.Dispose();
            Reset.Dispose();
            Bind.Dispose();
            PathMode.Dispose();
            disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
