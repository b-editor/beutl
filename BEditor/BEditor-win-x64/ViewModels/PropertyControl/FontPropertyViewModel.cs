using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BEditor.ViewModels.Helper;

using BEditor.NET.Data;
using BEditor.NET.Data.PropertyData;

namespace BEditor.ViewModels.PropertyControl {
    public class FontPropertyViewModel {
        public FontProperty Property { get; }
        public DelegateCommand<(object, object)> Command { get; }

        public FontPropertyViewModel(FontProperty property) {
            Property = property;
            Command = new DelegateCommand<(object, object)>(x => {
                UndoRedoManager.Do(new FontProperty.ChangeSelect(property, (BEditor.NET.Media.Font)x.Item2));
            });
        }
    }
}
