#nullable enable

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
public class ReferenceEditor : PropertyEditor
{
    public static readonly DirectProperty<ReferenceEditor, string?> TargetNameProperty =
        AvaloniaProperty.RegisterDirect<ReferenceEditor, string?>(
            nameof(TargetName),
            o => o.TargetName,
            (o, v) => o.TargetName = v,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly RoutedEvent<RoutedEventArgs> SelectTargetRequestedEvent =
        RoutedEvent.Register<ReferenceEditor, RoutedEventArgs>(
            nameof(SelectTargetRequested),
            RoutingStrategies.Bubble);

    private string? _targetName;
    private readonly CompositeDisposable _eventRevokers = new(2);

    public string? TargetName
    {
        get => _targetName;
        set => SetAndRaise(TargetNameProperty, ref _targetName, value);
    }

    public event EventHandler<RoutedEventArgs>? SelectTargetRequested
    {
        add => AddHandler(SelectTargetRequestedEvent, value);
        remove => RemoveHandler(SelectTargetRequestedEvent, value);
    }

    protected Button? TargetPickerButton { get; private set; }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _eventRevokers.Clear();
        base.OnApplyTemplate(e);

        TargetPickerButton = e.NameScope.Find<Button>("PART_TargetPickerButton");
        if (TargetPickerButton != null)
        {
            TargetPickerButton.AddDisposableHandler(Button.ClickEvent, OnTargetPickerButtonClick)
                .DisposeWith(_eventRevokers);
        }
    }

    private void OnTargetPickerButtonClick(object? sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(SelectTargetRequestedEvent));
    }
}
