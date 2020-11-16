using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BEditor.ViewModels.Helper;
using BEditor.Core.Data;
using BEditor.Core.Data.PropertyData;

namespace BEditor.ViewModels.PropertyControl
{
    public class FilePropertyViewModel
    {
        public FileProperty Property { get; }
        public DelegateCommand<Func<string, string, string>> Command { get; }

        public FilePropertyViewModel(FileProperty property)
        {
            Property = property;
            Command = new DelegateCommand<Func<string, string, string>>(x =>
            {
                var file = x?.Invoke((property.PropertyMetadata as FilePropertyMetadata)?.FilterName, (property.PropertyMetadata as FilePropertyMetadata)?.Filter);

                if (file != null)
                {
                    UndoRedoManager.Do(new FileProperty.ChangeFileCommand(property, file));
                }
            });
        }
    }
}
