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
    public class FilePropertyViewModel
    {
        public FilePropertyViewModel(FileProperty property)
        {
            Property = property;
            Command = new(x =>
            {
                var file = x?.Invoke(Property.PropertyMetadata?.FilterName, Property.PropertyMetadata?.Filter);

                if (file != null)
                {
                    CommandManager.Do(new FileProperty.ChangeFileCommand(Property, file));
                }
            });
            Reset = new(() =>
            {
                CommandManager.Do(new FileProperty.ChangeFileCommand(Property, Property.PropertyMetadata.DefaultFile));
            });
        }

        public FileProperty Property { get; }
        public DelegateCommand<Func<string, string, string>> Command { get; }
        public DelegateCommand Reset { get; }
    }
}
