using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Service
{
    public interface IFileDialogService
    {
        public bool ShowSaveFileDialog(SaveFileRecord record);
    }

    public record SaveFileRecord : FileDialogRecord
    {
        public SaveFileRecord(string filename = "") : base(new List<FileFilter>())
        {
            FileName = filename;
        }

        public string DefaultFileName { get; set; }
        public string FileName { get; set; }
    }
    public record FileFilter(string Name, string Extension);
    public record FileDialogRecord(List<FileFilter> Filters);
}
