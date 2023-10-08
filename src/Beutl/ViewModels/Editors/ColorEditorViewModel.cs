using Avalonia;

using Beutl.Controls.PropertyEditors;
using Beutl.Media;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using AColor = Avalonia.Media.Color;

namespace Beutl.ViewModels.Editors;

public sealed class ColorEditorViewModel : ValueEditorViewModel<Color>, IConfigureLivePreview
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

    public ReactivePropertySlim<bool> IsLivePreviewEnabled { get; } = new(true);

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is ColorEditor editor)
        {
            editor[!ColorEditor.ValueProperty] = Value2.ToBinding();
            editor[!ColorEditor.IsLivePreviewEnabledProperty] = IsLivePreviewEnabled.ToBinding();
            editor.ValueConfirmed += OnValueConfirmed;
            editor.ValueChanged += OnValueChanged;
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (sender is ColorEditor editor)
        {
            editor.Value = SetCurrentValueAndGetCoerced(editor.Value.ToMedia()).ToAvalonia();
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<AColor> args)
        {
            SetValue(args.OldValue.ToMedia(), args.NewValue.ToMedia());
        }
    }
}
