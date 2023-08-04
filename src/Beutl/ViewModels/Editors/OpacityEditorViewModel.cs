namespace Beutl.ViewModels.Editors;

public sealed class OpacityEditorViewModel : ValueEditorViewModel<float>
{
    public OpacityEditorViewModel(IAbstractProperty<float> property)
        : base(property)
    {
    }
}
