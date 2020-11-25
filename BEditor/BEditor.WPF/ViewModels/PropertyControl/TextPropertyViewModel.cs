using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.ViewModels.Helper;

namespace BEditor.ViewModels.PropertyControl
{
    public class TextPropertyViewModel
    {
        public TextPropertyViewModel(TextProperty property)
        {
            Property = property;
            Reset = new(() => CommandManager.Do(new TextProperty.ChangeTextCommand(Property, Property.PropertyMetadata.DefaultText)));
        }

        public TextProperty Property { get; }
        public DelegateCommand Reset { get; }
    }
}
