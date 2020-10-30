using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.ViewModels.Helper;
using BEditor.NET.Data;
using BEditor.NET.Data.PropertyData;

namespace BEditor.ViewModels.PropertyControl {
    public class SelectorPropertyViewModel {
        public SelectorProperty Property { get; }
        public DelegateCommand<(object, object)> Command { get; }

        public SelectorPropertyViewModel(SelectorProperty selector) {
            Property = selector;
            Command = new DelegateCommand<(object, object)>(x => UndoRedoManager.Do(new SelectorProperty.ChangeSelect(selector, (int)x.Item1)));
        }
    }
}
