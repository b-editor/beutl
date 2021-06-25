using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace BEditor.Models
{
    public sealed class FileDialogService : IFileDialogService
    {
        public async ValueTask<bool> ShowOpenFileDialogAsync(OpenFileRecord record)
        {
            if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var dialog = new OpenFileDialog
                {
                    Filters = record.Filters.ConvertAll(i => new FileDialogFilter
                    {
                        Extensions = i.Extensions.Select(i => i.TrimStart('.')).ToList(),
                        Name = i.Name
                    }),
                    InitialFileName = record.DefaultFileName,
                    AllowMultiple = false
                };

                var file = await dialog.ShowAsync(desktop.MainWindow);

                if (File.Exists(file?.FirstOrDefault()))
                {
                    record.FileName = file![0];

                    return true;
                }
            }

            return false;
        }

        public async ValueTask<bool> ShowSaveFileDialogAsync(SaveFileRecord record)
        {
            if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var dialog = new SaveFileDialog
                {
                    Filters = record.Filters.ConvertAll(i => new FileDialogFilter
                    {
                        Extensions = i.Extensions.Select(i => i.TrimStart('.')).ToList(),
                        Name = i.Name
                    }),
                    InitialFileName = record.DefaultFileName,
                };

                var file = await dialog.ShowAsync(desktop.MainWindow);

                if (!string.IsNullOrWhiteSpace(file))
                {
                    record.FileName = file;

                    return true;
                }
            }

            return false;
        }
    }
}