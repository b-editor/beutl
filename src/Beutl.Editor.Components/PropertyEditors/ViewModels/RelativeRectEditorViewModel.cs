using Avalonia;
using Beutl.Controls.PropertyEditors;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class RelativeRectEditorViewModel(IPropertyAdapter<Graphics.RelativeRect> property)
    : ValueEditorViewModel<Graphics.RelativeRect>(property), IConfigureUniformEditor, IRelativeUnitEditor
{
    public ReactivePropertySlim<bool> IsUniformEditorEnabled { get; } = new();

    IObservable<Graphics.RelativeUnit> IRelativeUnitEditor.Unit => Value.Select(value => value.Unit);

    void IRelativeUnitEditor.SetUnit(Graphics.RelativeUnit unit)
    {
        Graphics.RelativeRect value = GetEditingValue();
        SetValue(value, new Graphics.RelativeRect(value.Rect, unit));
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is RelativeRectEditor editor && !Disposables.IsDisposed)
        {
            editor.Bind(Vector4Editor.IsUniformProperty, IsUniformEditorEnabled.ToBinding())
                .DisposeWith(Disposables);
        }
    }
}
