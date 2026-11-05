using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Collections;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;
using TextBlock = Avalonia.Controls.TextBlock;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class ListItemLayoutTests
{
    // From the item's left edge.
    private const double GripCenter = 20;
    private const double NameStart = 40;

    [AvaloniaTest]
    public async Task An_items_rows_start_under_its_name_and_its_line_runs_under_its_grip()
    {
        var blur = new Blur();
        var group = new FilterEffectGroup { Children = { blur } };
        using var history = new HistoryScope(group);
        using var list = CreateList(new EnginePropertyAdapter<ICoreList<FilterEffect>>(group.Children, group), history);
        var window = new Window { Content = new ListEditor { DataContext = list }, Width = 480, Height = 600 };
        try
        {
            window.Show();
            await Settle();
            AssertLinesUp(await Expand(window, blur), window, itemLeft: 0);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task An_items_line_meets_its_grip_and_the_grip_of_the_next_item_alike()
    {
        var first = new Blur();
        var second = new Blur();
        var group = new FilterEffectGroup { Children = { first, second } };
        using var history = new HistoryScope(group);
        using var list = CreateList(new EnginePropertyAdapter<ICoreList<FilterEffect>>(group.Children, group), history);
        var window = new Window { Content = new ListEditor { DataContext = list }, Width = 480, Height = 600 };
        try
        {
            window.Show();
            await Settle();
            FilterEffectListItemEditor item = await Expand(window, first);
            FilterEffectListItemEditor next = window.GetVisualDescendants().OfType<FilterEffectListItemEditor>()
                .Single(i => i != item);

            TreeLineDecorator line = Line(item);
            Assert.That(line.Bounds.Height, Is.GreaterThan(0));
            Assert.That(Top(line, window), Is.EqualTo(Top(Grip(item), window) + Grip(item).Bounds.Height));
            Assert.That(Top(line, window) + line.Bounds.Height, Is.EqualTo(Top(Grip(next), window)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task An_item_of_a_nested_group_starts_its_grip_under_the_name_of_the_group()
    {
        var blur = new Blur();
        var inner = new FilterEffectGroup { Children = { blur } };
        var group = new FilterEffectGroup { Children = { inner } };
        using var history = new HistoryScope(group);
        using var list = CreateList(new EnginePropertyAdapter<ICoreList<FilterEffect>>(group.Children, group), history);
        var window = new Window { Content = new ListEditor { DataContext = list }, Width = 480, Height = 600 };
        try
        {
            window.Show();
            await Settle();
            FilterEffectListItemEditor outerItem = await Expand(window, inner);
            FilterEffectListItemEditor innerItem = await Expand(window, blur);

            Assert.That(Left(Grip(innerItem), window), Is.EqualTo(Left(Title(outerItem), window)));
            AssertLinesUp(innerItem, window, itemLeft: NameStart - 8);
            Assert.That(Left(Line(outerItem), window) + LineInset, Is.EqualTo(GripCenter));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task A_presenter_item_keeps_the_grip_and_the_delete_button_of_the_other_items()
    {
        var group = new FilterEffectGroup { Children = { new Blur(), new FilterEffectPresenter() } };
        using var history = new HistoryScope(group);
        using var list = CreateList(new EnginePropertyAdapter<ICoreList<FilterEffect>>(group.Children, group), history);
        var window = new Window { Content = new ListEditor { DataContext = list }, Width = 480, Height = 600 };
        try
        {
            window.Show();
            await Settle();
            FilterEffectListItemEditor[] items = window.GetVisualDescendants().OfType<FilterEffectListItemEditor>().ToArray();
            FilterEffectListItemEditor effect = items.Single(i => i.ReorderHandle is ToggleButton);
            FilterEffectListItemEditor presenter = items.Single(i => i != effect);
            ReferenceEditor reference = presenter.GetVisualDescendants().OfType<ReferenceEditor>().Single();

            Assert.That(Center(reference.ReorderHandle!, window), Is.EqualTo(GripCenter));
            Assert.That(Right(DeleteButton(presenter), window), Is.EqualTo(Right(DeleteButton(effect), window)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void The_host_of_an_editor_without_a_list_item_style_lines_up_with_the_other_items()
    {
        var child = new Border { Height = 40 };
        var host = new ListItemEditorHost();
        host.SetChild(child);
        var window = new Window { Content = host, Width = 480, Height = 600 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            Assert.That(Center(host.ReorderHandle!, window), Is.EqualTo(GripCenter));
            Assert.That(Left(host.GetVisualDescendants().OfType<TextBlock>().First(), window), Is.EqualTo(NameStart));
            Assert.That(Left(child, window), Is.EqualTo(NameStart - 8));
            Assert.That(Left(host.GetVisualDescendants().OfType<TreeLineDecorator>().Single(), window) + LineInset, Is.EqualTo(GripCenter));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public async Task Only_an_effect_that_is_not_a_group_has_a_line_beside_its_content(bool isGroup)
    {
        var shape = new RectShape { FilterEffect = { Value = isGroup ? new FilterEffectGroup { Children = { new Blur() } } : new Blur() } };
        using var history = new HistoryScope(shape);
        using var viewModel = new FilterEffectEditorViewModel(new EnginePropertyAdapter<FilterEffect?>(shape.FilterEffect, shape));
        viewModel.Accept(Services(history));
        viewModel.IsExpanded.Value = true;
        var window = new Window { Content = new FilterEffectEditor { DataContext = viewModel }, Width = 480, Height = 600 };
        try
        {
            window.Show();
            await Settle();

            TreeLineDecorator line = window.GetVisualDescendants().OfType<EffectEditorContent>().Single()
                .GetVisualDescendants().OfType<TreeLineDecorator>().First();
            Assert.That(line.IsVisible, Is.EqualTo(!isGroup));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_figures_of_a_geometry_are_not_beside_the_line_of_its_own_properties()
    {
        var geometry = new PathGeometry();
        geometry.Figures.Add(new PathFigure());
        var shape = new GeometryShape { Data = { Value = geometry } };
        using var history = new HistoryScope(shape);
        using var viewModel = new GeometryEditorViewModel(new EnginePropertyAdapter<Geometry?>(shape.Data, shape));
        viewModel.Accept(Services(history));
        viewModel.IsExpanded.Value = true;
        var window = new Window { Content = new GeometryEditor { DataContext = viewModel }, Width = 480, Height = 600 };
        try
        {
            window.Show();
            await Settle();

            PathFigureListItemEditor figure = window.GetVisualDescendants().OfType<PathFigureListItemEditor>().Single();
            Assert.That(figure.GetVisualAncestors().OfType<TreeLineDecorator>(), Is.Empty);
            Assert.That(Center(Grip((ToggleButton)figure.ReorderHandle!), window), Is.EqualTo(GripCenter));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(640)]
    [TestCase(300)]
    public async Task An_open_transform_shows_its_first_property_in_place_of_its_name(int width)
    {
        var move = new TranslateTransform();
        var scale = new ScaleTransform();
        var group = new TransformGroup { Children = { move, scale } };
        using var history = new HistoryScope(group);
        using var list = CreateList(new EnginePropertyAdapter<ICoreList<Transform>>(group.Children, group), history);
        var window = new Window { Content = new ListEditor { DataContext = list }, Width = width, Height = 600 };
        try
        {
            window.Show();
            await Settle();

            foreach (Transform transform in new Transform[] { move, scale })
            {
                TransformListItemEditor item = Item(window, transform);
                ContentPresenter grip = Grip((ToggleButton)item.ReorderHandle!);
                TextBlock label = Labels(item).First();
                Assert.That(item.FindControl<ToggleButton>("reorderHandle")!.IsEffectivelyVisible, Is.False);
                Assert.That(Center(grip, window), Is.EqualTo(GripCenter));
                Assert.That(Left(label, window), Is.EqualTo(NameStart));
                // A row that stacks its label over its value has the icon beside the label, cut off by nothing above.
                Assert.That(Middle(grip, window), Is.EqualTo(Middle(label, window)).Within(2));
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task A_transforms_line_runs_from_under_its_icon_to_its_last_row_and_a_single_row_has_none()
    {
        var move = new TranslateTransform();
        var scale = new ScaleTransform();
        var group = new TransformGroup { Children = { move, scale } };
        using var history = new HistoryScope(group);
        using var list = CreateList(new EnginePropertyAdapter<ICoreList<Transform>>(group.Children, group), history);
        var window = new Window { Content = new ListEditor { DataContext = list }, Width = 640, Height = 600 };
        try
        {
            window.Show();
            await Settle();

            Assert.That(Line(Item(window, move)).Bounds.Height, Is.Zero);

            TransformListItemEditor item = Item(window, scale);
            TreeLineDecorator line = Line(item);
            ContentPresenter grip = Grip((ToggleButton)item.ReorderHandle!);
            Panel content = item.FindControl<Panel>("content")!;
            Assert.That(Left(line, window) + LineInset, Is.EqualTo(GripCenter));
            Assert.That(Top(line, window), Is.EqualTo(Top(grip, window) + grip.Bounds.Height));
            Assert.That(Top(line, window) + line.Bounds.Height, Is.EqualTo(Top(content, window) + content.Bounds.Height));
            Assert.That(line.LineBrush, Is.Not.Null);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Clicking_the_icon_of_a_transform_closes_it_to_a_row_with_its_name_and_back()
    {
        var scale = new ScaleTransform();
        var group = new TransformGroup { Children = { scale } };
        using var history = new HistoryScope(group);
        using var list = CreateList(new EnginePropertyAdapter<ICoreList<Transform>>(group.Children, group), history);
        var window = new Window { Content = new ListEditor { DataContext = list }, Width = 640, Height = 600 };
        try
        {
            window.Show();
            await Settle();
            TransformListItemEditor item = Item(window, scale);
            var icon = (ToggleButton)item.ReorderHandle!;
            ToggleButton nameRow = item.FindControl<ToggleButton>("reorderHandle")!;
            Panel content = item.FindControl<Panel>("content")!;
            Assert.That(icon, Is.Not.SameAs(nameRow));

            // The rows swap at once: fading the open rows out under the row with the name made them jump.
            HeadlessTestHelpers.Click(Grip(icon), ticks: 1);
            Assert.That(content.IsVisible, Is.False);
            Assert.That(nameRow.IsEffectivelyVisible, Is.True);
            await Settle();
            Assert.That(item.ReorderHandle, Is.SameAs(nameRow));
            // The row with the name shows the buttons to hide and remove the transform.
            Button[] buttons = NameRowButtons(item);
            Assert.That(buttons, Has.Length.EqualTo(2));
            Assert.That(buttons, Has.All.Matches<Button>(b => b.IsEffectivelyVisible));
            HeadlessTestHelpers.Click(buttons.OfType<ToggleButton>().Single(), ticks: 3);
            Assert.That(scale.IsEnabled.Value, Is.False);
            HeadlessTestHelpers.Click(buttons.OfType<ToggleButton>().Single(), ticks: 3);
            Assert.That(scale.IsEnabled.Value, Is.True);

            HeadlessTestHelpers.Click(Grip(nameRow), ticks: 1);
            Assert.That(content.IsVisible, Is.True);
            Assert.That(content.Opacity, Is.EqualTo(1));
            Assert.That(nameRow.IsEffectivelyVisible, Is.False);
            await Settle();
            Assert.That(item.ReorderHandle, Is.SameAs(icon));
            Assert.That(buttons, Has.None.Matches<Button>(b => b.IsEffectivelyVisible));

            HeadlessTestHelpers.Click(Grip(icon), ticks: 3);
            await Settle();
            HeadlessTestHelpers.Click(buttons.Single(b => b is not ToggleButton), ticks: 3);
            await Settle();
            Assert.That(group.Children, Is.Empty);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Dragging_the_icon_of_a_transform_reorders_it_and_leaves_it_open()
    {
        var move = new TranslateTransform();
        var scale = new ScaleTransform();
        var group = new TransformGroup { Children = { move, scale } };
        using var history = new HistoryScope(group);
        using var list = CreateList(new EnginePropertyAdapter<ICoreList<Transform>>(group.Children, group), history);
        var window = new Window { Content = new ListEditor { DataContext = list }, Width = 640, Height = 600 };
        try
        {
            window.Show();
            await Settle();
            ContentPresenter from = Grip((ToggleButton)Item(window, move).ReorderHandle!);
            Point start = from.TranslatePoint(new Point(from.Bounds.Width / 2, from.Bounds.Height / 2), window)!.Value;
            // Past the middle of the next item, which has two rows.
            Point end = start + new Vector(0, 60);

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(0, 8), RawInputModifiers.LeftMouseButton);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);
            await Settle();

            Assert.That(group.Children, Is.EqualTo(new Transform[] { scale, move }));
            Assert.That(((TransformEditorViewModel)Item(window, move).DataContext!).IsExpanded.Value, Is.True);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task A_right_click_on_any_row_of_a_transform_offers_hiding_and_removing_it()
    {
        var move = new TranslateTransform();
        var scale = new ScaleTransform();
        var group = new TransformGroup { Children = { move, scale } };
        using var history = new HistoryScope(group);
        using var list = CreateList(new EnginePropertyAdapter<ICoreList<Transform>>(group.Children, group), history);
        var window = new Window { Content = new ListEditor { DataContext = list }, Width = 640, Height = 600 };
        try
        {
            window.Show();
            await Settle();
            TransformListItemEditor item = Item(window, scale);
            var flyout = (FAMenuFlyout)item.ContextFlyout!;
            var root = (Grid)item.Content!;
            Assert.That(root.Opacity, Is.EqualTo(1));

            HeadlessTestHelpers.Click(Labels(item).Last(), button: MouseButton.Right);
            Assert.That(flyout.IsOpen, Is.True);
            FAToggleMenuFlyoutItem visible = flyout.Items.OfType<FAToggleMenuFlyoutItem>().Single();
            Assert.That(visible.Text, Is.EqualTo(Strings.Visible));
            Assert.That(visible.IsChecked, Is.True);
            HeadlessTestHelpers.Click(visible);
            await Settle();
            Assert.That(scale.IsEnabled.Value, Is.False);
            Assert.That(flyout.IsOpen, Is.False);
            // With no button left to show it, the row itself tells that the transform is hidden.
            Assert.That(root.Opacity, Is.LessThan(1));

            HeadlessTestHelpers.Click(Grip((ToggleButton)item.ReorderHandle!), button: MouseButton.Right);
            Assert.That(flyout.IsOpen, Is.True);
            Assert.That(visible.IsChecked, Is.False);
            HeadlessTestHelpers.Click(flyout.Items.OfType<FAMenuFlyoutItem>().Single(i => i.Text == Strings.Remove));
            await Settle();
            Assert.That(group.Children.Single(), Is.SameAs(move));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task A_transform_in_a_node_keeps_the_row_with_its_name_when_open()
    {
        var scale = new ScaleTransform();
        var group = new TransformGroup { Children = { scale } };
        using var history = new HistoryScope(group);
        var list = new ListEditorViewModel<Transform>(new EnginePropertyAdapter<ICoreList<Transform>>(group.Children, group));
        using (list)
        {
            list.Accept(new ServiceVisitor(
                (typeof(HistoryManager), history.History),
                (typeof(IPropertyEditorFactory), new PropertyEditorFactoryAdapter(TestShell.Extensions)),
                (typeof(ExtensionProvider), TestShell.Extensions),
                (typeof(GraphNode), new LayerInputNode())));
            list.IsExpanded.Value = true;
            var window = new Window { Content = new ListEditor { DataContext = list }, Width = 640, Height = 600 };
            try
            {
                window.Show();
                await Settle();
                TransformListItemEditor item = Item(window, scale);
                var viewModel = (TransformEditorViewModel)item.DataContext!;
                Assert.That(viewModel.IsExpanded.Value, Is.False);

                viewModel.IsExpanded.Value = true;
                await Settle();

                ToggleButton nameRow = item.FindControl<ToggleButton>("reorderHandle")!;
                Assert.That(item.ReorderHandle, Is.SameAs(nameRow));
                Assert.That(nameRow.IsEffectivelyVisible, Is.True);
                Assert.That(NameRowButtons(item), Has.All.Matches<Button>(b => b.IsEffectivelyVisible));
                Assert.That(item.FindControl<Panel>("content")!.IsVisible, Is.True);
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaTest]
    public async Task A_presenter_transform_lines_up_with_the_boxes_of_the_other_rows()
    {
        var move = new TranslateTransform();
        var presenter = new TransformPresenter();
        var group = new TransformGroup { Children = { move, presenter } };
        using var history = new HistoryScope(group);
        using var list = CreateList(new EnginePropertyAdapter<ICoreList<Transform>>(group.Children, group), history);
        var window = new Window { Content = new ListEditor { DataContext = list }, Width = 640, Height = 600 };
        try
        {
            window.Show();
            await Settle();

            TransformListItemEditor item = Item(window, presenter);
            ReferenceEditor reference = item.GetVisualDescendants().OfType<ReferenceEditor>().Single();
            Control picker = reference.GetVisualDescendants().OfType<DropDownButton>().Single();
            Border box = Item(window, move).GetVisualDescendants().OfType<Border>()
                .First(b => b.Name == "PART_BackgroundBorder" && b.IsEffectivelyVisible);
            Assert.That(item.ReorderHandle, Is.SameAs(reference.ReorderHandle));
            Assert.That(Center(reference.ReorderHandle!, window), Is.EqualTo(GripCenter));
            Assert.That(Right(picker, window), Is.EqualTo(Right(box, window)));
        }
        finally
        {
            window.Close();
        }
    }

    // The decorator draws its line this far into itself.
    private const double LineInset = 8;

    private static TransformListItemEditor Item(Window window, Transform transform)
        => window.GetVisualDescendants().OfType<TransformListItemEditor>()
            .Single(i => ((TransformEditorViewModel)i.DataContext!).Value.Value == transform);

    private static Button[] NameRowButtons(TransformListItemEditor item)
        => ((Panel)item.FindControl<ToggleButton>("reorderHandle")!.Parent!).Children.OfType<Button>()
            .Where(b => b.Name != "reorderHandle").ToArray();

    private static IEnumerable<TextBlock> Labels(Control item) => item.GetVisualDescendants().OfType<TextBlock>()
        .Where(t => t.Name == "PART_HeaderTextBlock" && t.IsEffectivelyVisible);

    private static TreeLineDecorator Line(TransformListItemEditor item)
        => item.FindControl<Panel>("content")!.GetVisualDescendants().OfType<TreeLineDecorator>().First();

    private static double Middle(Control control, Visual root) => Top(control, root) + control.Bounds.Height / 2;

    private static void AssertLinesUp(FilterEffectListItemEditor item, Visual root, double itemLeft)
    {
        Assert.That(Center(Grip(item), root), Is.EqualTo(itemLeft + GripCenter));
        Assert.That(Left(Title(item), root), Is.EqualTo(itemLeft + NameStart));

        TextBlock label = item.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Name == "PART_HeaderTextBlock" && t.IsEffectivelyVisible);
        Assert.That(Left(label, root), Is.EqualTo(itemLeft + NameStart));

        Assert.That(Left(Line(item), root) + LineInset, Is.EqualTo(itemLeft + GripCenter));
        Assert.That(Line(item).LineBrush, Is.Not.Null);
    }

    private static TreeLineDecorator Line(FilterEffectListItemEditor item)
        => item.FindControl<Panel>("content")!.Children.OfType<TreeLineDecorator>().Single();

    private static ContentPresenter Grip(FilterEffectListItemEditor item) => Grip((ToggleButton)item.ReorderHandle!);

    private static ContentPresenter Grip(ToggleButton header) => header.GetVisualDescendants().OfType<ContentPresenter>()
        .Single(c => c.Name == "TagContentPresenter");

    private static ContentPresenter Title(FilterEffectListItemEditor item) => ((ToggleButton)item.ReorderHandle!)
        .GetVisualDescendants().OfType<ContentPresenter>().Single(c => c.Name == "ContentPresenter");

    private static Button DeleteButton(FilterEffectListItemEditor item) => item.GetVisualDescendants().OfType<Button>()
        .Last(b => b.IsEffectivelyVisible && b is not ToggleButton && b is not DropDownButton);

    private static double Left(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.X;

    private static double Top(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;

    private static double Right(Control control, Visual root) => Left(control, root) + control.Bounds.Width;

    private static double Center(Control control, Visual root) => Left(control, root) + control.Bounds.Width / 2;

    private static async Task<FilterEffectListItemEditor> Expand(Window window, FilterEffect effect)
    {
        FilterEffectListItemEditor item = window.GetVisualDescendants().OfType<FilterEffectListItemEditor>()
            .Single(i => ((FilterEffectEditorViewModel)i.DataContext!).Value.Value == effect);
        ((FilterEffectEditorViewModel)item.DataContext!).IsExpanded.Value = true;
        await Settle();
        return item;
    }

    private static async Task Settle()
    {
        await Task.Delay(350);
        HeadlessTestHelpers.Render(3);
    }

    private static ServiceVisitor Services(HistoryScope history) => new(
        (typeof(HistoryManager), history.History),
        (typeof(IPropertyEditorFactory), new PropertyEditorFactoryAdapter(TestShell.Extensions)),
        (typeof(ExtensionProvider), TestShell.Extensions));

    private static ListEditorViewModel<T> CreateList<T>(IPropertyAdapter<ICoreList<T>> adapter, HistoryScope history)
    {
        var list = new ListEditorViewModel<T>(adapter);
        list.Accept(Services(history));
        list.IsExpanded.Value = true;
        return list;
    }
}
