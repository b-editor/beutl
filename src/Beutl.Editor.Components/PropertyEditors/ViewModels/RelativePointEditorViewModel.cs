using Avalonia;
using Beutl.Controls.PropertyEditors;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class RelativePointEditorViewModel(IPropertyAdapter<Graphics.RelativePoint> property)
    : ValueEditorViewModel<Graphics.RelativePoint>(property), IConfigureUniformEditor, IRelativeUnitEditor
{
    public ReactivePropertySlim<bool> IsUniformEditorEnabled { get; } = new();

    IObservable<Graphics.RelativeUnit> IRelativeUnitEditor.Unit => Value.Select(value => value.Unit);

    void IRelativeUnitEditor.SetUnit(Graphics.RelativeUnit unit)
    {
        Graphics.RelativePoint value = GetEditingValue();
        SetValue(value, new Graphics.RelativePoint(value.Point, unit));
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is RelativePointEditor editor && !Disposables.IsDisposed)
        {
            editor.Bind(Vector2Editor.IsUniformProperty, IsUniformEditorEnabled.ToBinding())
                .DisposeWith(Disposables);
        }
    }
}
