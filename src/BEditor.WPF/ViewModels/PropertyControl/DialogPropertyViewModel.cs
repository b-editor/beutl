
using BEditor.Data.Property;

using Reactive.Bindings;

namespace BEditor.ViewModels.PropertyControl
{
    public class DialogPropertyViewModel
    {
        public DialogPropertyViewModel(DialogProperty property)
        {
            Property = property;
            ClickCommand.Subscribe(() => Property.Show());
        }

        public DialogProperty Property { get; }
        public ReactiveCommand ClickCommand { get; } = new();
    }
}