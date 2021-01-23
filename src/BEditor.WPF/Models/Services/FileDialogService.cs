using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using BEditor.Core.Service;
using Microsoft.Win32;
using System.Text;

namespace BEditor.Models.Services
{
    public class FileDialogService : IFileDialogService
    {
        public bool ShowSaveFileDialog(SaveFileRecord record)
        {
            var fileDialog = new SaveFileDialog();
            fileDialog.FileName = record.DefaultFileName;
            fileDialog.AddExtension = true;

            StringBuilder builder = new();
            foreach (var item in record.Filters)
            {
                builder.Append(item.Name);
                builder.Append('|');

                var isFirst = true;
                foreach (var ext in item.Extensions)
                {
                    if (!isFirst) builder.Append(';');

                    builder.Append("*.");
                    builder.Append(ext.Value);

                    isFirst = false;
                }

                builder.Append('|');
            }
            builder.Append("All Files|*.*");
            fileDialog.Filter = builder.ToString();

            var result = fileDialog.ShowDialog();

            record.FileName = fileDialog.FileName;

            return result ?? false;
        }
    }
}
