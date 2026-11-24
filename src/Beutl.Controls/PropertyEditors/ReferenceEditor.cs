using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;

using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;

[TemplatePart("PART_TargetPickerButton", typeof(Button))]
public partial class ReferenceEditor : PropertyEditor
{
    public static readonly RoutedEvent<RoutedEventArgs> SelectTargetRequestedEvent =
        RoutedEvent.Register<ReferenceEditor, RoutedEventArgs>(
            nameof(SelectTargetRequested),
            RoutingStrategies.Bubble);

    private readonly CompositeDisposable _eventRevokers = new(2);

    [GeneratedDirectProperty(DefaultBindingMode = BindingMode.TwoWay)]
    public partial string? TargetName { get; set; }

    public event EventHandler<RoutedEventArgs>? SelectTargetRequested
    {
        add => AddHandler(SelectTargetRequestedEvent, value);
        remove => RemoveHandler(SelectTargetRequestedEvent, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _eventRevokers.Clear();
        base.OnApplyTemplate(e);

        Button? targetPickerButton = e.NameScope.Find<Button>("PART_TargetPickerButton");
        if (targetPickerButton != null)
        {
            targetPickerButton.AddDisposableHandler(Button.ClickEvent, OnTargetPickerButtonClick)
                .DisposeWith(_eventRevokers);
        }
    }

    private void OnTargetPickerButtonClick(object? sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(SelectTargetRequestedEvent));
    }
}
