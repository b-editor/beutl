using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

using BeUtl.Controls.Extensions;

namespace BeUtl.Controls;

[PseudoClasses(":dragging", ":lockdrag")]
public partial class DraggableTabItem : TabItem
{
    private Button _closeButton;

    public DraggableTabItem()
    {
        Closing += OnClosing;
    }

    static DraggableTabItem()
    {
        CanBeDraggedProperty.Changed.AddClassHandler<DraggableTabItem>((x, e) => x.OnCanDraggablePropertyChanged(x, e));
        IsSelectedProperty.Changed.AddClassHandler<DraggableTabItem>((x, _) => UpdatePseudoClass(x));
        IsClosableProperty.Changed.Subscribe(e =>
        {
            if (e.Sender is DraggableTabItem a && a._closeButton != null)
            {
                a._closeButton.IsVisible = a.IsClosable;
            }
        });
    }

    private static void UpdatePseudoClass(DraggableTabItem item)
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
