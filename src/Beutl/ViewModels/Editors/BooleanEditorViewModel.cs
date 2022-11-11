using Beutl.Framework;

namespace Beutl.ViewModels.Editors;

public sealed class BooleanEditorViewModel : ValueEditorViewModel<bool>
{
    public BooleanEditorViewModel(IAbstractProperty<bool> property)
        : base(property)
    {
    }
}
