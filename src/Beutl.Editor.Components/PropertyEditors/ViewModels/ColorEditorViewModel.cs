using Avalonia;

using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.PropertyEditors;
using Beutl.Media;

using Reactive.Bindings;

using AColor = Avalonia.Media.Color;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class ColorEditorViewModel(IPropertyAdapter<Color> property)
    : PropertyEditorViewModel<Color, AColor>(property), IConfigureLivePreview
{
    public ReactivePropertySlim<bool> IsLivePreviewEnabled { get; } = new(true);

    protected override AColor ToEdit(Color value) => value.ToAvaColor();

    protected override Color FromEdit(AColor edit) => edit.ToBtlColor();

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is ColorEditor editor && !Disposables.IsDisposed)
        {
            editor.Bind(ColorEditor.IsLivePreviewEnabledProperty, IsLivePreviewEnabled.ToBinding())
                .DisposeWith(Disposables);
        }
    }
}
