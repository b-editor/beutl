using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BEditor.ViewModels.Helper;

using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Core.Media;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Command;

namespace BEditor.ViewModels.PropertyControl
{
    public class FontPropertyViewModel
    {
        public FontPropertyViewModel(FontProperty property)
        {
            Property = property;
            Command = new DelegateCommand<(object, object)>(x =>
            {
                CommandManager.Do(new FontProperty.ChangeSelectCommand(property, (FontRecord)x.Item2));
            });
            Reset = new(() => CommandManager.Do(new FontProperty.ChangeSelectCommand(Property, Property.PropertyMetadata.SelectItem)));
        }

        public FontProperty Property { get; }
        public DelegateCommand<(object, object)> Command { get; }
        public DelegateCommand Reset { get; }
    }
}
