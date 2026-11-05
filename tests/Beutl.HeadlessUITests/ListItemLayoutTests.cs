using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
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
using Beutl.Media;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using TextBlock = Avalonia.Controls.TextBlock;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;

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

    // The decorator draws its line this far into itself.
    private const double LineInset = 8;

    private static void AssertLinesUp(FilterEffectListItemEditor item, Visual root, double itemLeft)
    {
        Assert.That(Center(Grip(item), root), Is.EqualTo(itemLeft + GripCenter));
        Assert.That(Left(Title(item), root), Is.EqualTo(itemLeft + NameStart));

        TextBlock label = item.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Name == "PART_HeaderTextBlock" && t.IsEffectivelyVisible);
        Assert.That(Left(label, root), Is.EqualTo(itemLeft + NameStart));

        Assert.That(Left(Line(item), root) + LineInset, Is.EqualTo(itemLeft + GripCenter));
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
