using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using BEditorNext.Controls.Extensions;

namespace BEditorNext.Controls;

[PseudoClasses(":dragging", ":lockdrag")]
public partial class DraggableTabItem : TabItem
{
    private Button CloseButton;

    public DraggableTabItem()
    {
        Closing += OnClosing;
    }

    static DraggableTabItem()
    {
        CanBeDraggedProperty.Changed.AddClassHandler<DraggableTabItem>((x, e) => x.OnCanDraggablePropertyChanged(x, e));
        IsSelectedProperty.Changed.AddClassHandler<DraggableTabItem>((x, e) => UpdatePseudoClass(x, e));
        IsClosableProperty.Changed.Subscribe(e =>
        {
            if (e.Sender is DraggableTabItem a && a.CloseButton != null)
            {
                a.CloseButton.IsVisible = a.IsClosable;
            }
        });
    }

    private static void UpdatePseudoClass(DraggableTabItem item, AvaloniaPropertyChangedEventArgs e)
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

        CloseButton = e.NameScope.Find<Button>("PART_CloseButton");
        if (IsClosable != false)
        {
            CloseButton.Click += CloseButton_Click;
        }
        else
        {
            CloseButton.IsVisible = false;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(CloseButtonClickEvent));
        Close();
    }
}
