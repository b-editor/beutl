using System;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;

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

            IsFullPath.Value = property.Mode is FilePathType.FullPath;
            IsRelPath.Value = !IsFullPath.Value;

            IsFullPath.Subscribe(i => Property.Mode = i ? FilePathType.FullPath : FilePathType.FromProject).AddTo(_disposables);

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

        public ReactivePropertySlim<bool> IsFullPath { get; } = new();

        public ReactivePropertySlim<bool> IsRelPath { get; } = new();

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
            IsFullPath.Dispose();
            IsRelPath.Dispose();
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}