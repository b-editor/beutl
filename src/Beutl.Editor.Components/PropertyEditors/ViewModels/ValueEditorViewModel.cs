using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.PropertyEditors;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

// An editor whose control edits the property's own value, which it shows as Value.
public class ValueEditorViewModel<T> : PropertyEditorViewModel<T, T>
{
    public ValueEditorViewModel(IPropertyAdapter<T> property)
        : base(property)
    {
        Value = ShownValue;
    }

    public ReadOnlyReactiveProperty<T> Value { get; }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (!IsDisposed && visitor is PropertyEditor editor
            && editor is IValueEditorControl<T> and (ValueStringEditor<T> or VectorEditorBase))
            DragSession.Connect(editor, this, EndValuePreview, CompleteElementRepair);
    }

    protected override T ToEdit(T value) => value;

    protected override T FromEdit(T edit) => edit;
}
