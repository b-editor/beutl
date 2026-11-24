using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Graphics.Effects;
using Beutl.Language;
using Beutl.Services;
using FluentIcons.Common;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.PropertyEditors.Views;

internal sealed class PuppetPinsEditor : ListEditor
{
    private readonly CompositeDisposable _subscriptions = [];
    private ListEditorViewModel<PuppetPin>? _pins;
    private Puppet? _effect;
    private IPreviewPuppetPinEditor? _editor;
    private CancellationTokenSource? _placement;

    public PuppetPinsEditor() : base(alwaysExpanded: true)
    {
        RemoveButton.IsVisible = true;
        ToolTip.SetTip(RemoveButton, GraphicsStrings.Puppet_RemovePin);
        AutomationProperties.SetName(RemoveButton, GraphicsStrings.Puppet_RemovePin);
        RemoveButton.Click += RemoveSelectedPin;
    }

    protected override Type StyleKeyOverride => typeof(ListEditor);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Connect();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Disconnect();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (this.IsAttachedToVisualTree()) Connect();
    }

    private void Connect()
    {
        Disconnect();
        _pins = DataContext as ListEditorViewModel<PuppetPin>;
        _effect = _pins?.PropertyAdapter.GetEngineProperty()?.GetOwnerObject() as Puppet;
        _editor = _pins?.GetService<IPreviewPuppetPinEditor>();
        if (_pins is { IsDisposed: false } pins)
        {
            pins.IsExpanded.Value = true;
            pins.CanEditProperty.Subscribe(_ => UpdateButtons()).DisposeWith(_subscriptions);
        }
        _editor?.SelectedPin.Subscribe(_ => UpdateButtons()).DisposeWith(_subscriptions);
        UpdateButtons();
    }

    private void Disconnect()
    {
        _placement?.Cancel();
        _placement = null;
        _subscriptions.Clear();
        _pins = null;
        _effect = null;
        _editor = null;
    }

    private bool CanPlace => _pins is { IsDisposed: false, EditsSeveralElements: false, CanEditProperty.Value: true }
        && _effect is not null && _editor is not null;

    private int SelectedPinIndex => _pins is { IsDisposed: false } pins && _effect is { } effect
        && _editor?.SelectedPin.Value is { } pin && pins.CanReplace(pin)
        ? effect.Pins.IndexOf(pin)
        : -1;

    private void UpdateButtons()
    {
        bool placing = _placement is { IsCancellationRequested: false };
        string label = placing ? GraphicsStrings.Puppet_FinishPlacement : GraphicsStrings.Puppet_AddPin;
        AddButton.IsEnabled = placing || CanPlace;
        AddIcon.Icon = placing ? Icon.Checkmark : Icon.Add;
        ToolTip.SetTip(AddButton, label);
        AutomationProperties.SetName(AddButton, label);
        RemoveButton.IsEnabled = SelectedPinIndex >= 0;
    }

    private void RemoveSelectedPin(object? sender, RoutedEventArgs e)
    {
        int index = SelectedPinIndex;
        if (index < 0) return;

        Puppet effect = _effect!;
        IPreviewPuppetPinEditor editor = _editor!;
        PuppetPin selected = effect.Pins[index];
        _placement?.Cancel();
        _pins!.RemoveItem(index);
        if (effect.Pins.Count > 0 && !effect.Pins.Contains(selected))
            editor.SelectPin(effect, effect.Pins[Math.Min(index, effect.Pins.Count - 1)]);
    }

    internal override async Task AddItemAsync(IListEditorViewModel viewModel, Control? button)
    {
        if (_placement is { IsCancellationRequested: false } active)
        {
            active.Cancel();
            UpdateButtons();
            return;
        }
        if (!CanPlace || !ReferenceEquals(viewModel, _pins)) return;

        var pins = _pins!;
        var effect = _effect!;
        var editor = _editor!;
        using var lifetime = new CancellationTokenSource();
        _placement = lifetime;
        UpdateButtons();
        TopLevel? root = TopLevel.GetTopLevel(this);
        root?.AddHandler(KeyDownEvent, CancelOnEscape, RoutingStrategies.Tunnel, handledEventsToo: true);
        try
        {
            bool available = await editor.PlacePinsAsync(effect, AddPin, lifetime.Token);
            if (!available && ReferenceEquals(_placement, lifetime) && !lifetime.IsCancellationRequested)
                ControlNotifications.FromDataContextOrDefault(this)
                    .ShowWarning(GraphicsStrings.Puppet, GraphicsStrings.Puppet_PlacementUnavailable);
        }
        finally
        {
            root?.RemoveHandler(KeyDownEvent, CancelOnEscape);
            if (ReferenceEquals(_placement, lifetime))
            {
                _placement = null;
                UpdateButtons();
            }
        }

        bool AddPin(PuppetPin pin)
        {
            if (!ReferenceEquals(_pins, pins) || !CanPlace || lifetime.IsCancellationRequested) return false;
            pins.AddItem(pin);
            return effect.Pins.Contains(pin);
        }

        void CancelOnEscape(object? sender, KeyEventArgs args)
        {
            if (args.Key != Key.Escape || lifetime.IsCancellationRequested) return;
            lifetime.Cancel();
            args.Handled = true;
        }
    }
}
