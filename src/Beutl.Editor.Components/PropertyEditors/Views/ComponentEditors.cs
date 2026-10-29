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
using RelativePoint = Beutl.Graphics.RelativePoint;
using RelativeRect = Beutl.Graphics.RelativeRect;
using RelativeUnit = Beutl.Graphics.RelativeUnit;

namespace Beutl.Editor.Components.PropertyEditors.Views;

// Replaces the combined picker with scalar editors while the property's components are separated.
internal sealed class ComponentEditors : StackPanel, IDisposable
{
    private readonly PropertyEditorViewModel _owner;
    private readonly Control _combined;
    private readonly DockPanel _header = new() { Margin = new Thickness(8, 0, 4, 0) };
    private readonly StackPanel _components = new();
    private readonly TreeLineDecorator _tree = new()
    {
        [!TreeLineDecorator.LineBrushProperty] = new DynamicResourceExtension("DividerStrokeColorDefaultBrush")
    };
    private readonly List<IPropertyEditorContext> _contexts = [];
    private readonly IDisposable _subscription;
    private readonly IDisposable _editability;
    private readonly EnumEditor? _unitEditor;
    private readonly CompositeDisposable _unitSubscriptions = [];

    public ComponentEditors(PropertyEditorViewModel owner, Control combined)
    {
        _owner = owner;
        _combined = combined;
        if (combined is PropertyEditor { MenuContent: not null })
        {
            var menu = new PropertyEditorMenu { DataContext = owner };
            DockPanel.SetDock(menu, Dock.Right);
            _header.Children.Add(menu);
        }
        _header.Children.Add(new TextBlock
        {
            Text = owner.Header,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        Children.Add(combined);
        Children.Add(_header);
        _tree.Child = _components;
        Children.Add(_tree);
        _unitEditor = owner switch
        {
            ValueEditorViewModel<RelativePoint> point => CreateUnitEditor(point, value => value.Unit,
                (value, unit) => new RelativePoint(value.Point, unit)),
            ValueEditorViewModel<RelativeRect> rect => CreateUnitEditor(rect, value => value.Unit,
                (value, unit) => new RelativeRect(value.Rect, unit)),
            _ => null
        };
        _subscription = owner.SeparatedComponents.Subscribe(Rebuild);
        _editability = owner.CanEditProperty.Subscribe(enabled => _components.IsEnabled = enabled);
    }

    private void Rebuild(IComponentAnimation? animation)
    {
        _combined.IsVisible = animation == null;
        _header.IsVisible = animation != null;
        _tree.IsVisible = animation != null;
        _components.Children.Clear();
        foreach (var context in _contexts) context.Dispose();
        _contexts.Clear();
        if (animation is not CoreObject obj) return;
        if (_unitEditor != null) _components.Children.Add(_unitEditor);
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
                _components.Children.Add(control);
            }
        }
    }

    private EnumEditor CreateUnitEditor<T>(ValueEditorViewModel<T> owner, Func<T, RelativeUnit> getUnit,
        Func<T, RelativeUnit, T> withUnit)
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
        _unitSubscriptions.Add(owner.Value.Select(getUnit).DistinctUntilChanged().Subscribe(unit =>
        {
            updating = true;
            try { editor.SelectedIndex = (int)unit; }
            finally { updating = false; }
        }));
        _unitSubscriptions.Add(editor.Bind(PropertyEditor.IsReadOnlyProperty, owner.CanEdit.Select(editable => !editable)));
        editor.ValueConfirmed += (_, e) =>
        {
            if (updating || !owner.CanEdit.Value || owner.SeparatedComponents.Value == null
                || e is not PropertyEditorValueChangedEventArgs<int> args) return;
            T value = owner.GetEditingValue()!;
            owner.SetValue(value, withUnit(value, (RelativeUnit)args.NewValue));
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
        _editability.Dispose();
        _unitSubscriptions.Dispose();
        foreach (var context in _contexts) context.Dispose();
        _contexts.Clear();
    }
}
