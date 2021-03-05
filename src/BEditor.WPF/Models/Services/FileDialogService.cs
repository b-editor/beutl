using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

using Microsoft.Win32;

namespace BEditor.Models.Services
{
    public class FileDialogService : IFileDialogService
    {
        private static string GenFilter(OpenFileRecord record)
        {
            StringBuilder builder = new();
            foreach (var item in record.Filters)
            {
                builder.Append(item.Name);
                builder.Append('|');

                builder.Append(string.Join(';', item.Extensions.Select(i => "*." + i.Value)));

                builder.Append('|');
            }
            builder.Append("All Files|*.*");
            return builder.ToString();
        }
        private static string GenFilter(SaveFileRecord record)
        {
            StringBuilder builder = new();
            foreach (var item in record.Filters)
            {
                builder.Append(item.Name);
                builder.Append('|');

                builder.Append(string.Join(';', item.Extensions.Select(i => "*." + i.Value)));

                builder.Append('|');
            }
            builder.Append("All Files|*.*");
            return builder.ToString();
        }

        public bool ShowOpenFileDialog(OpenFileRecord record)
        {
            var fileDialog = new OpenFileDialog
            {
                FileName = record.DefaultFileName,
                Filter = GenFilter(record),
                AddExtension = true
            };

            var result = fileDialog.ShowDialog();

            record.FileName = fileDialog.FileName;

            return result ?? false;
        }
        public bool ShowSaveFileDialog(SaveFileRecord record)
        {
            var fileDialog = new SaveFileDialog
            {
                FileName = record.DefaultFileName,
                Filter = GenFilter(record),
                AddExtension = true
            };

            var result = fileDialog.ShowDialog();

            record.FileName = fileDialog.FileName;

            return result ?? false;
        }
    }
}
