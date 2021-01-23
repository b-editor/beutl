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
        public bool ShowOpenFileDialog(OpenFileRecord record);
    }

    public record SaveFileRecord : FileDialogRecord
    {
        public SaveFileRecord(string filename = "") : base(new List<FileFilter>())
        {
            FileName = filename;
        }

        public string DefaultFileName { get; set; } = "";
        public string FileName { get; set; }
    }
    public record OpenFileRecord : FileDialogRecord
    {
        public OpenFileRecord() : base(new List<FileFilter>())
        {

        }

        public string DefaultFileName { get; set; } = "";
        public string FileName { get; set; } = "";
    }
    public record FileExtension(string Value);
    public record FileFilter(string Name, IEnumerable<FileExtension> Extensions);
    public record FileDialogRecord(List<FileFilter> Filters);
}
