using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
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
        var window = new Window
        {
            Width = 200,
            Height = 200,
            Content = new StackPanel { Children = { first, second, items } },
        };

        try
        {
            window.Show();
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
            window.Close();
            HeadlessTestHelpers.Settle();
        }

        void Click(Control control)
        {
            Point point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            HeadlessTestHelpers.Settle();
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
