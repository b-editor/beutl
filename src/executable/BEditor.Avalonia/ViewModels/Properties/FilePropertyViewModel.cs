using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;

using Avalonia;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Views.Properties;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.Properties
{
    public sealed class FilePropertyViewModel : IDisposable
    {
        private readonly CompositeDisposable _disposables = new();

        public FilePropertyViewModel(FileProperty property)
        {
            Property = property;

            PathMode = property.ObserveProperty(p => p.Mode)
                .Select(i => (int)i)
                .ToReactiveProperty()
                .AddTo(_disposables);

            PathMode.Subscribe(i => Property.Mode = (FilePathType)i).AddTo(_disposables);

            Command.Subscribe(async _ =>
            {
                var file = await OpenDialogAsync(Property.PropertyMetadata?.Filter);

                if (file != null)
                {
                    Property.ChangeFile(file).Execute();
                }
            }).AddTo(_disposables);

            Reset.Where(_ => Property.Value != (Property.PropertyMetadata?.DefaultFile ?? string.Empty))
                .Subscribe(_ => Property.ChangeFile(Property.PropertyMetadata?.DefaultFile ?? string.Empty).Execute())
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

        ~FilePropertyViewModel()
        {
            Dispose();
        }

        public FileProperty Property { get; }

        public ReactiveCommand<Func<string, string, string>> Command { get; } = new();

        public ReactiveCommand Reset { get; } = new();

        public ReactiveCommand Bind { get; } = new();

        public ReactiveCommand CopyID { get; } = new();

        public ReactiveProperty<int> PathMode { get; }

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

        private async Task<string?> OpenDialogAsync(FileFilter? filter)
        {
            var dialog = Property.ServiceProvider?.GetService<IFileDialogService>();
            if (dialog is null) return null;
            var record = new OpenFileRecord();

            if (filter is not null)
            {
                record.Filters.Add(filter);
            }

            // ダイアログを表示する
            if (await dialog.ShowOpenFileDialogAsync(record))
            {
                return record.FileName;
            }

            return null;
        }
    }
}