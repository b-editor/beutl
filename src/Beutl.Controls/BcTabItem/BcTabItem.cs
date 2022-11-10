using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;

using Beutl.Controls.Behaviors;
using Beutl.Controls.Extensions;

namespace Beutl.Controls;

[PseudoClasses(":dragging", ":lockdrag")]
public partial class BcTabItem : TabItem
{
    private readonly ItemDragBehavior _dragBehavior;
    private Button _closeButton;

    public BcTabItem()
    {
        Closing += OnClosing;
        _dragBehavior = new ItemDragBehavior();
        _dragBehavior.Attach(this);
    }

    static BcTabItem()
    {
        TabStripPlacementProperty.Changed.AddClassHandler<BcTabItem>((x, _) => x.OnTabStripPlacementChanged());
        CanBeDraggedProperty.Changed.AddClassHandler<BcTabItem>((x, e) => x.OnCanDraggablePropertyChanged(x, e));
        IsSelectedProperty.Changed.AddClassHandler<BcTabItem>((x, _) => UpdatePseudoClass(x));
        IsClosableProperty.Changed.Subscribe(e =>
        {
            if (e.Sender is BcTabItem a && a._closeButton != null)
            {
                a._closeButton.IsVisible = a.IsClosable;
            }
        });
    }

    private void OnTabStripPlacementChanged()
    {
        _dragBehavior.Orientation = TabStripPlacement is Dock.Top or Dock.Bottom
            ? Orientation.Horizontal
            : Orientation.Vertical;
    }

    private static void UpdatePseudoClass(BcTabItem item)
    {
        if (!item.IsSelected)
        {
            item.PseudoClasses.Remove(":dragging");
        }
    }

    internal bool CloseCore()
    {
        if (Parent is TabControl x)
        {
            try
            {
                x.CloseTab(this);
                return true;
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    public bool Close()
    {
        RaiseEvent(new RoutedEventArgs(ClosingEvent));
        return CloseCore();
    }

    protected void OnCanDraggablePropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (CanBeDragged)
        {
            PseudoClasses.Add(":lockdrag");
        }
        else
        {
            PseudoClasses.Remove(":lockdrag");
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _closeButton = e.NameScope.Find<Button>("PART_CloseButton");
        if (IsClosable != false)
        {
            _closeButton.Click += CloseButton_Click;
        }
        else
        {
            _closeButton.IsVisible = false;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(CloseButtonClickEvent));
        Close();
    }
}
