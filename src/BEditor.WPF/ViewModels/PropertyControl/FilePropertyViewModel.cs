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
using BEditor.Views.PropertyControls;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public sealed class FilePropertyViewModel : IDisposable
    {
        private readonly CompositeDisposable disposables = new();

        public FilePropertyViewModel(FileProperty property)
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
                var file = OpenDialog(Property.PropertyMetadata?.Filter ?? null);

                if (file != null)
                {
                    Property.ChangeFile(file).Execute();
                }
            }).AddTo(disposables);
            Reset.Subscribe(() => Property.ChangeFile(Property.PropertyMetadata?.DefaultFile ?? "").Execute()).AddTo(disposables);
            Bind.Subscribe(() =>
            {
                var window = new BindSettings(new BindSettingsViewModel<string>(property));
                window.ShowDialog();
            }).AddTo(disposables);
        }
        ~FilePropertyViewModel()
        {
            Dispose();
        }

        public ReadOnlyReactivePropertySlim<FilePropertyMetadata?> Metadata { get; }
        public FileProperty Property { get; }
        public ReactiveCommand<Func<string, string, string>> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
        public ReactiveCommand Bind { get; } = new();
        public ReactiveProperty<int> PathMode { get; }

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

        private string? OpenDialog(FileFilter? filter)
        {
            var dialog = Property.ServiceProvider?.GetService<IFileDialogService>();
            if (dialog is null) return null;
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
