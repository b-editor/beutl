using Beutl.Framework;

namespace Beutl.ViewModels.Editors;

public sealed class EnumEditorViewModel<T> : ValueEditorViewModel<T>
    where T : struct, Enum
{
    public EnumEditorViewModel(IAbstractProperty<T> property)
        : base(property)
    {
    }
}
