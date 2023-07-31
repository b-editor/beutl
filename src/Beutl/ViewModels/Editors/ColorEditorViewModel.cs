using Avalonia;

using Beutl.Controls.PropertyEditors;
using Beutl.Framework;
using Beutl.Media;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using AColor = Avalonia.Media.Color;

namespace Beutl.ViewModels.Editors;

public sealed class ColorEditorViewModel : ValueEditorViewModel<Color>
{
    public ColorEditorViewModel(IAbstractProperty<Color> property)
        : base(property)
    {
        Value2 = Value
            .Select(x => x.ToAvalonia())
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<AColor> Value2 { get; }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is ColorEditor editor)
        {
            editor[!ColorEditor.ValueProperty] = Value2.ToBinding();
            editor.ValueChanged += OnValueChanged;
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<AColor> args)
        {
            SetValue(args.OldValue.ToMedia(), args.NewValue.ToMedia());
        }
    }
}
