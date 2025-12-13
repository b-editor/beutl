using Avalonia;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;
using Beutl.Media;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public sealed class GradingColorEditorViewModel : ValueEditorViewModel<GradingColor>, IConfigureLivePreview
{
    public GradingColorEditorViewModel(IPropertyAdapter<GradingColor> property)
        : base(property)
    {
    }

    public ReactivePropertySlim<bool> IsLivePreviewEnabled { get; } = new(true);

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is GradingColorEditor editor && !Disposables.IsDisposed)
        {
            editor.Bind(GradingColorEditor.ValueProperty, Value.ToBinding())
                .DisposeWith(Disposables);
            editor.Bind(GradingColorEditor.IsLivePreviewEnabledProperty, IsLivePreviewEnabled.ToBinding())
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueChangedEvent, OnValueChanged)
                .DisposeWith(Disposables);
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (sender is GradingColorEditor editor)
        {
            editor.Value = SetCurrentValueAndGetCoerced(editor.Value);
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<GradingColor> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }
}
