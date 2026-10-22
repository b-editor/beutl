using Avalonia;
using Beutl.Controls.PropertyEditors;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class RelativePointEditorViewModel(IPropertyAdapter<Graphics.RelativePoint> property)
    : ValueEditorViewModel<Graphics.RelativePoint>(property), IConfigureUniformEditor
{
    public ReactivePropertySlim<bool> IsUniformEditorEnabled { get; } = new();

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
