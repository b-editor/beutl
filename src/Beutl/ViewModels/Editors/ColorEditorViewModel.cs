using Avalonia;
using Avalonia.Interactivity;

using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Media;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using AColor = Avalonia.Media.Color;

namespace Beutl.ViewModels.Editors;

public sealed class ColorEditorViewModel : ValueEditorViewModel<Color>, IConfigureLivePreview
{
    public ColorEditorViewModel(IPropertyAdapter<Color> property)
        : base(property)
    {
        Value2 = Value
            .Select(x => x.ToAvaColor())
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<AColor> Value2 { get; }

    public ReactivePropertySlim<bool> IsLivePreviewEnabled { get; } = new(true);

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is ColorEditor editor && !Disposables.IsDisposed)
        {
            editor.Bind(ColorEditor.ValueProperty, Value2.ToBinding())
                .DisposeWith(Disposables);
            editor.Bind(ColorEditor.IsLivePreviewEnabledProperty, IsLivePreviewEnabled.ToBinding())
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueChangedEvent, OnValueChanged)
                .DisposeWith(Disposables);
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (sender is ColorEditor editor)
        {
            editor.Value = SetCurrentValueAndGetCoerced(editor.Value.ToBtlColor()).ToAvaColor();
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<AColor> args)
        {
            SetValue(args.OldValue.ToBtlColor(), args.NewValue.ToBtlColor());
        }
    }
}
