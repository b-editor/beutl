using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Beutl.Animation;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.PropertyEditors;
using Beutl.PropertyAdapters;
using RelativeUnit = Beutl.Graphics.RelativeUnit;

namespace Beutl.Editor.Components.PropertyEditors.Views;

// Replaces the combined picker with scalar editors while the property's components are separated.
internal sealed class ComponentEditors : StackPanel, IDisposable
{
    private readonly PropertyEditorViewModel _owner;
    private readonly Control _combined;
    private readonly bool _hasMenu;
    private readonly List<IPropertyEditorContext> _contexts = [];
    private readonly IDisposable _subscription;
    // Built when the components are first separated, which most properties never are.
    private SeparatedChrome? _chrome;

    public ComponentEditors(PropertyEditorViewModel owner, Control combined)
    {
        _owner = owner;
        _combined = combined;
        _hasMenu = combined is PropertyEditor { MenuContent: not null };
        Children.Add(combined);
        _subscription = owner.SeparatedComponents.Subscribe(Rebuild);
    }

    private void Rebuild(IComponentAnimation? animation)
    {
        _combined.IsVisible = animation == null;
        if (animation == null && _chrome == null) return;

        SeparatedChrome chrome = EnsureSeparatedChrome();
        chrome.Header.IsVisible = animation != null;
        chrome.Tree.IsVisible = animation != null;
        chrome.Components.Children.Clear();
        foreach (var context in _contexts) context.Dispose();
        _contexts.Clear();
        if (animation is not CoreObject obj) return;
        if (chrome.UnitEditor != null) chrome.Components.Children.Add(chrome.UnitEditor);
        foreach (IProperty property in animation.Components)
        {
            var adapter = PropertyAdapterFactory.CreateAdapter(property, obj);
            var context = PropertyEditorResolver.CreateContext(adapter, [PropertyEditorExtension.Instance], PropertyEditorSurface.Panel);
            if (context == null) continue;
            _contexts.Add(context);
            context.Accept(new ChildVisitor(_owner));
            if (PropertyEditorHost.TryCreateControl(context, PropertyEditorSurface.Panel, out Control? control))
            {
                control.DataContext = context;
                chrome.Components.Children.Add(control);
            }
        }
    }

    // The header, with the property's menu and name, and the components' tree below it, after the combined picker.
    private SeparatedChrome EnsureSeparatedChrome()
    {
        if (_chrome != null) return _chrome;

        var header = new DockPanel { Margin = new Thickness(8, 0, 4, 0) };
        if (_hasMenu)
        {
            var menu = new PropertyEditorMenu { DataContext = _owner };
            DockPanel.SetDock(menu, Dock.Right);
            header.Children.Add(menu);
        }
        header.Children.Add(new TextBlock
        {
            Text = _owner.Header,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        var components = new StackPanel();
        var tree = new TreeLineDecorator
        {
            [!TreeLineDecorator.LineBrushProperty] = new DynamicResourceExtension("DividerStrokeColorDefaultBrush"),
            Child = components
        };
        Children.Add(header);
        Children.Add(tree);
        var subscriptions = new CompositeDisposable();
        EnumEditor? unitEditor = _owner is IRelativeUnitEditor unit ? CreateUnitEditor(unit, subscriptions) : null;
        subscriptions.Add(_owner.CanEditProperty.Subscribe(enabled => components.IsEnabled = enabled));
        return _chrome = new SeparatedChrome(header, tree, components, unitEditor, subscriptions);
    }

    private EnumEditor CreateUnitEditor(IRelativeUnitEditor unit, CompositeDisposable subscriptions)
    {
        var editor = new EnumEditor
        {
            Header = NodeGraphStrings.Port_Unit,
            EditorStyle = PropertyEditorStyle.Compact,
            Items =
            [
                new EnumItem(Strings.RelativeUnit_Relative, null, RelativeUnit.Relative),
                new EnumItem(Strings.RelativeUnit_Absolute, null, RelativeUnit.Absolute)
            ]
        };
        bool updating = false;
        subscriptions.Add(unit.Unit.DistinctUntilChanged().Subscribe(value =>
        {
            updating = true;
            try { editor.SelectedIndex = (int)value; }
            finally { updating = false; }
        }));
        subscriptions.Add(editor.Bind(PropertyEditor.IsReadOnlyProperty, _owner.CanEdit.Select(editable => !editable)));
        editor.ValueConfirmed += (_, e) =>
        {
            if (updating || !_owner.CanEdit.Value || _owner.SeparatedComponents.Value == null
                || e is not PropertyEditorValueChangedEventArgs<int> args) return;
            unit.SetUnit((RelativeUnit)args.NewValue);
        };
        return editor;
    }

    // The parent may receive its clock/history after its control is created.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        foreach (var context in _contexts) context.Accept(new ChildVisitor(_owner));
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _chrome?.Subscriptions.Dispose();
        foreach (var context in _contexts) context.Dispose();
        _contexts.Clear();
    }

    private sealed record SeparatedChrome(
        DockPanel Header, TreeLineDecorator Tree, StackPanel Components, EnumEditor? UnitEditor, CompositeDisposable Subscriptions);
}
