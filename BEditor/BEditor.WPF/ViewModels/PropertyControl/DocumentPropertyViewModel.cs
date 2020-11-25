
using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.ViewModels.Helper;

namespace BEditor.ViewModels.PropertyControl
{
    public class DocumentPropertyViewModel
    {
        public DocumentPropertyViewModel(DocumentProperty property)
        {
            Property = property;
            Reset = new(() =>
            {
                CommandManager.Do(new DocumentProperty.TextChangeCommand(Property, Property.PropertyMetadata.DefaultText));
            });
        }

        public DocumentProperty Property { get; }
        public DelegateCommand Reset { get; }
    }
}
