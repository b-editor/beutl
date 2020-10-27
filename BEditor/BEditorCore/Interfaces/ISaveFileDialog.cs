using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditorCore.Interfaces {
    public interface ISaveFileDialog {
        public List<(string name, string extension)> Filters { get; }
        public string DefaultFileName { get; set; }
        public string FileName { get; set; }

        public bool ShowDialog();
    }
}
