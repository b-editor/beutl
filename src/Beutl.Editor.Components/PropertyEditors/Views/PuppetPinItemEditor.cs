using System.Collections.Specialized;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.PropertyEditors;
using Beutl.Graphics.Effects;
using Beutl.Language;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.PropertyEditors.Views;

// The shared list's drag behavior uses the Vector2Editor's reorder handle.
internal sealed class PuppetPinItemEditor : ContentControl, IListItemEditor
{
    private readonly CompositeDisposable _subscriptions = [];
    private readonly CompositeDisposable _rowSubscriptions = [];

    public static readonly DirectProperty<PuppetPinItemEditor, Control?> ReorderHandleProperty =
        AvaloniaProperty.RegisterDirect<PuppetPinItemEditor, Control?>(nameof(ReorderHandle), editor => editor.ReorderHandle);

    public Control? ReorderHandle
    {
        get;
        private set => SetAndRaise(ReorderHandleProperty, ref field, value);
    }

    public event EventHandler? DeleteRequested;

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
        if (DataContext is PuppetPinItemViewModel model)
            model.Position.Subscribe(position => SetPositionEditor(model, position)).DisposeWith(_subscriptions);
    }

    private void Disconnect()
    {
        _subscriptions.Clear();
        _rowSubscriptions.Clear();
        ReorderHandle = null;
        Content = null;
    }

    private void SetPositionEditor(PuppetPinItemViewModel model, PropertyEditorViewModel? position)
    {
        _rowSubscriptions.Clear();
        ReorderHandle = null;
        Content = null;
        if (position?.PropertyAdapter.GetEngineProperty()?.GetOwnerObject() is not PuppetPin pin
            || !PropertyEditorHost.TryCreateControl(position, PropertyEditorSurface.ListItem, out Control? control)
            || control is not PropertyEditor editor)
            return;

        var item = model.PropertyAdapter as IListItemAccessor;
        Puppet? effect = pin.FindHierarchicalParent<Puppet>();
        IPreviewPuppetPinEditor? preview = model.EditsSeveralElements ? null : model.GetService<IPreviewPuppetPinEditor>();
        editor.DataContext = position;
        editor.MenuContent = new PropertyEditorMenu { DataContext = position, EditComponentsInFlyout = true };
        editor.Classes.Add("numbered-list-item");
        UpdateNumber();
        editor.GetObservable(PropertyEditor.ReorderHandleProperty)
            .Subscribe(handle => ReorderHandle = handle).DisposeWith(_rowSubscriptions);
        editor.DeleteRequested += DeletePin;
        Disposable.Create(() => editor.DeleteRequested -= DeletePin).DisposeWith(_rowSubscriptions);

        if (item?.List is INotifyCollectionChanged list)
        {
            NotifyCollectionChangedEventHandler changed = (_, _) => UpdateNumber();
            list.CollectionChanged += changed;
            Disposable.Create(() => list.CollectionChanged -= changed).DisposeWith(_rowSubscriptions);
        }
        if (preview is not null && effect is not null)
        {
            preview.SelectedPin.Subscribe(selected => editor.Classes.Set("selected", ReferenceEquals(pin, selected)))
                .DisposeWith(_rowSubscriptions);
            editor.AddHandler(PointerPressedEvent, SelectOnPress, RoutingStrategies.Tunnel);
            Disposable.Create(() => editor.RemoveHandler(PointerPressedEvent, SelectOnPress)).DisposeWith(_rowSubscriptions);
            editor.GetObservable(IsKeyboardFocusWithinProperty).Skip(1).Where(focused => focused)
                .Subscribe(_ => preview.SelectPin(effect, pin)).DisposeWith(_rowSubscriptions);
        }
        Content = editor;

        void UpdateNumber()
        {
            int index = item?.Index ?? -1;
            editor.Header = index < 0 ? GraphicsStrings.Puppet_Pin : (index + 1).ToString(CultureInfo.CurrentCulture);
            AutomationProperties.SetName(editor, index < 0 ? GraphicsStrings.Puppet_Pin : $"{GraphicsStrings.Puppet_Pin} {index + 1}");
        }

        void SelectOnPress(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(editor).Properties.IsLeftButtonPressed)
                preview!.SelectPin(effect!, pin);
        }

        void DeletePin(object? sender, EventArgs e)
        {
            DeleteRequested?.Invoke(this, e);
        }
    }
}
