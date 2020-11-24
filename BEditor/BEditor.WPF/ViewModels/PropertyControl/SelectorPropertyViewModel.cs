using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.ViewModels.Helper;
using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Command;

namespace BEditor.ViewModels.PropertyControl
{
    public class SelectorPropertyViewModel
    {
        public SelectorPropertyViewModel(SelectorProperty selector)
        {
            Property = selector;
            Command = new(x => CommandManager.Do(new SelectorProperty.ChangeSelectCommand(Property, (int)x.Item1)));
            Reset = new(() => CommandManager.Do(new SelectorProperty.ChangeSelectCommand(Property, Property.PropertyMetadata.DefaultIndex)));
        }

        public SelectorProperty Property { get; }
        public DelegateCommand<(object, object)> Command { get; }
        public DelegateCommand Reset { get; }
    }
}
