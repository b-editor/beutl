
using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;

using Reactive.Bindings;

namespace BEditor.ViewModels.PropertyControl
{
    public class DocumentPropertyViewModel
    {
        public DocumentPropertyViewModel(DocumentProperty property)
        {
            Property = property;
            Reset.Subscribe(() => CommandManager.Do(new DocumentProperty.TextChangeCommand(Property, Property.PropertyMetadata.DefaultText)));
        }

        public DocumentProperty Property { get; }
        public ReactiveCommand Reset { get; } = new();
    }
}
