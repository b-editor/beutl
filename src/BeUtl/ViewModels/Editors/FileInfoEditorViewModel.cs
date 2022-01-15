using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BeUtl.ProjectSystem;

namespace BeUtl.ViewModels.Editors;

public sealed class FileInfoEditorViewModel : BaseEditorViewModel<FileInfo>
{
    public FileInfoEditorViewModel(Setter<FileInfo> setter)
        : base(setter)
    {
    }
}
