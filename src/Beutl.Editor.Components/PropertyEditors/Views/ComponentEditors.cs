using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Beutl.Animation;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.PropertyEditors;
using Beutl.PropertyAdapters;

namespace Beutl.Editor.Components.PropertyEditors.Views;

// Replaces the combined picker with scalar editors while the property's components are separated.
internal sealed class ComponentEditors : StackPanel, IDisposable
{
    private readonly PropertyEditorViewModel _owner;
    private readonly Control _combined;
    private readonly DockPanel _header = new() { Margin = new Thickness(8, 0, 4, 0) };
    private readonly StackPanel _components = new() { Margin = new Thickness(12, 0, 0, 0) };
    private readonly List<IPropertyEditorContext> _contexts = [];
    private readonly IDisposable _subscription;
    private readonly IDisposable _editability;

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
        Children.Add(_components);
        _subscription = owner.SeparatedComponents.Subscribe(Rebuild);
        _editability = owner.CanEditProperty.Subscribe(enabled => _components.IsEnabled = enabled);
    }

    private void Rebuild(IComponentAnimation? animation)
    {
        _combined.IsVisible = animation == null;
        _header.IsVisible = animation != null;
        _components.IsVisible = animation != null;
        _components.Children.Clear();
        foreach (var context in _contexts) context.Dispose();
        _contexts.Clear();
        if (animation is not CoreObject obj) return;
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
        foreach (var context in _contexts) context.Dispose();
        _contexts.Clear();
    }
}
