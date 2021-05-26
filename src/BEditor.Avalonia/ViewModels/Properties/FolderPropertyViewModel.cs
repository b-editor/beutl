using System;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Views.Properties;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.Properties
{
    public sealed class FolderPropertyViewModel : IDisposable
    {
        private readonly CompositeDisposable _disposables = new();

        public FolderPropertyViewModel(FolderProperty property)
        {
            Property = property;

            PathMode = property.ObserveProperty(p => p.Mode)
                .Select(i => (int)i)
                .ToReactiveProperty()
                .AddTo(_disposables);

            PathMode.Subscribe(i => Property.Mode = (FilePathType)i).AddTo(_disposables);

            Command.Subscribe(async _ =>
            {
                var file = await OpenDialogAsync();

                if (file != null)
                {
                    Property.ChangeFolder(file).Execute();
                }
            }).AddTo(_disposables);

            Reset.Where(_ => Property.Value != (Property.PropertyMetadata?.Default ?? string.Empty))
                .Subscribe(_ => Property.ChangeFolder(Property.PropertyMetadata?.Default ?? string.Empty).Execute())
                .AddTo(_disposables);

            Bind.Subscribe(async () =>
            {
                var window = new SetBinding
                {
                    DataContext = new SetBindingViewModel<string>(Property)
                };
                await window.ShowDialog(App.GetMainWindow());
            }).AddTo(_disposables);

            CopyID.Subscribe(async () => await Application.Current.Clipboard.SetTextAsync(Property.Id.ToString())).AddTo(_disposables);
        }

        ~FolderPropertyViewModel()
        {
            Dispose();
        }

        public FolderProperty Property { get; }

        public ReactiveCommand Command { get; } = new();

        public ReactiveCommand Reset { get; } = new();

        public ReactiveCommand Bind { get; } = new();

        public ReactiveCommand CopyID { get; } = new();

        public ReactiveProperty<int> PathMode { get; }

        private static async Task<string?> OpenDialogAsync()
        {
            // ダイアログのインスタンスを生成
            var dialog = new OpenFolderDialog();

            // ダイアログを表示する
            if (await dialog.ShowAsync(App.GetMainWindow()) is var name && Directory.Exists(name))
            {
                return name;
            }

            return null;
        }

        public void Dispose()
        {
            Command.Dispose();
            Reset.Dispose();
            Bind.Dispose();
            CopyID.Dispose();
            PathMode.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}