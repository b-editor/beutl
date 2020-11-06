using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BEditor.ViewModels.Helper;

using BEditor.Core.Data;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Media;

namespace BEditor.ViewModels.PropertyControl {
    public class FontPropertyViewModel {
        public FontProperty Property { get; }
        public DelegateCommand<(object, object)> Command { get; }

        public FontPropertyViewModel(FontProperty property) {
            Property = property;
            Command = new DelegateCommand<(object, object)>(x => {
                UndoRedoManager.Do(new FontProperty.ChangeSelect(property, (FontRecord)x.Item2));
            });
        }
    }
}
