using Avalonia;

using Beutl.Controls.PropertyEditors;
using Beutl.Framework;
using Beutl.Media;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using AColor = Avalonia.Media.Color;

namespace Beutl.ViewModels.Editors;

public sealed class ColorEditorViewModel : BaseEditorViewModel<Color>
{
    public ColorEditorViewModel(IAbstractProperty<Color> property)
        : base(property)
    {
        Value = property.GetObservable()
            .Select(x => x.ToAvalonia())
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<AColor> Value { get; }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is ColorEditor editor)
        {
            editor[!ColorEditor.ValueProperty] = Value.ToBinding();
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
