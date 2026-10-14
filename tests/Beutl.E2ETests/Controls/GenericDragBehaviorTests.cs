using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Xaml.Interactivity;
using Beutl.Controls.Behaviors;
using Beutl.Testing.Headless;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class GenericDragBehaviorTests
{
    [AvaloniaTest]
    public void Changing_drag_control_moves_handlers_and_null_detaches_them()
    {
        var first = new Border { Width = 80, Height = 40, Background = Brushes.White };
        var second = new Border { Width = 80, Height = 40, Background = Brushes.White };
        var item = new Border { Width = 80, Height = 40 };
        var items = new ItemsControl { Items = { item } };
        var behavior = new ProbeDragBehavior { DragControl = first };
        Interaction.GetBehaviors(item).Add(behavior);
        var host = new ControlHost(new StackPanel { Children = { first, second, items } }, 200, 200);
        Window window = host.Window;

        try
        {
            HeadlessTestHelpers.Render();
            Assert.That(item.FindLogicalAncestorOfType<ItemsControl>(), Is.SameAs(items));

            Click(first);
            Assert.That(behavior.PressedCount, Is.EqualTo(1));

            behavior.DragControl = second;
            Click(first);
            Assert.That(behavior.PressedCount, Is.EqualTo(1));
            Click(second);
            Assert.That(behavior.PressedCount, Is.EqualTo(2));

            behavior.DragControl = null;
            Click(second);
            Assert.That(behavior.PressedCount, Is.EqualTo(2));
        }
        finally
        {
            Interaction.GetBehaviors(item).Remove(behavior);
            host.Dispose();
        }

        void Click(Control control)
        {
            Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void Changing_drag_control_while_pressed_releases_capture_and_cancels_drag()
    {
        var first = new Border { Width = 80, Height = 40, Background = Brushes.White };
        var second = new Border { Width = 80, Height = 40, Background = Brushes.White };
        var item = new Border { Width = 80, Height = 40 };
        var behavior = new ProbeDragBehavior { DragControl = first };
        Interaction.GetBehaviors(item).Add(behavior);
        IPointer? pointer = null;
        first.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
            RoutingStrategies.Bubble, handledEventsToo: true);
        var host = new ControlHost(
            new StackPanel { Children = { first, second, new ItemsControl { Items = { item } } } }, 200, 200);
        Window window = host.Window;

        try
        {
            HeadlessTestHelpers.Render();
            Point point = first.TranslatePoint(new Point(first.Bounds.Width / 2, first.Bounds.Height / 2), window)!.Value;

            window.MouseDown(point, MouseButton.Left);
            Assert.That(pointer?.Captured, Is.SameAs(first));

            behavior.DragControl = second;
            Assert.That(pointer?.Captured, Is.Null);
            bool active = (bool)typeof(GenericDragBehavior)
                .GetField("_enableDrag", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(behavior)!;
            Assert.That(active, Is.False);
        }
        finally
        {
            window.MouseUp(new Point(20, 20), MouseButton.Left);
            Interaction.GetBehaviors(item).Remove(behavior);
            host.Dispose();
        }
    }

    private sealed class ProbeDragBehavior : GenericDragBehavior
    {
        public int PressedCount { get; private set; }

        protected override ContentPresenter? OnFindDraggedContainer()
        {
            PressedCount++;
            return null;
        }
    }
}
