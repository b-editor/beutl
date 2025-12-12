using Avalonia;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;
using Beutl.Media;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using AColor = Avalonia.Media.Color;

namespace Beutl.ViewModels.Editors.Specialized;

public sealed class ColorWheelEditorViewModel : ValueEditorViewModel<Color>, IConfigureLivePreview
{
    public ColorWheelEditorViewModel(IPropertyAdapter<Color> property)
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
        if (visitor is ColorWheelEditor wheel && !Disposables.IsDisposed)
        {
            wheel.Bind(ColorWheelEditor.ValueProperty, Value2.ToBinding())
                .DisposeWith(Disposables);
            wheel.Bind(ColorWheelEditor.IsLivePreviewEnabledProperty, IsLivePreviewEnabled.ToBinding())
                .DisposeWith(Disposables);
            wheel.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);
            wheel.AddDisposableHandler(PropertyEditor.ValueChangedEvent, OnValueChanged)
                .DisposeWith(Disposables);
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
