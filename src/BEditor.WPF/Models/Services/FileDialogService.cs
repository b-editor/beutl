using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using BEditor.Core.Service;

using Microsoft.WindowsAPICodePack.Dialogs;

namespace BEditor.Models.Services
{
    public class FileDialogService : IFileDialogService
    {
        public bool ShowSaveFileDialog(SaveFileRecord record)
        {
            var fileDialog = new CommonSaveFileDialog();

            fileDialog.DefaultFileName = record.DefaultFileName;
            foreach (var item in record.Filters)
            {
                fileDialog.Filters.Add(new CommonFileDialogFilter(item.Name, item.Extension));
            }

            var result = fileDialog.ShowDialog();

            record.FileName = fileDialog.FileName;

            if (result == CommonFileDialogResult.Ok)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
