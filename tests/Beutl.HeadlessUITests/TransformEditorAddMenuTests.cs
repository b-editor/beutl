using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class TransformEditorAddMenuTests
{
    private static ServiceVisitor WithExtensions(HistoryScope history) => new(
        (typeof(HistoryManager), history.History),
        (typeof(IPropertyEditorFactory), new PropertyEditorFactoryAdapter(TestShell.Extensions)),
        (typeof(ExtensionProvider), TestShell.Extensions));

    private static TransformEditorViewModel CreateViewModel(RectShape shape, HistoryScope history)
    {
        var viewModel = new TransformEditorViewModel(
            new SimplePropertyAdapter<Transform?>((SimpleProperty<Transform?>)shape.Transform, shape));
        viewModel.Accept(WithExtensions(history));
        return viewModel;
    }

    private static void ClickAdd(TransformEditor editor, KnownTransformType type)
    {
        Window window = editor.GetVisualAncestors().OfType<Window>().Single();
        editor.GetVisualDescendants().OfType<EffectEditorHeaderButtons>().Single()
            .RaiseEvent(new RoutedEventArgs(EffectEditorHeaderButtons.ClickEvent));
        HeadlessTestHelpers.Render();
        window.GetVisualDescendants().OfType<FAMenuFlyoutItem>()
            .Single(item => item.Tag is KnownTransformType tag && tag == type)
            .RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
    }

    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 480, Height = 600 };
        window.Show();
        HeadlessTestHelpers.Render();
        return window;
    }

    [AvaloniaTest]
    public void The_add_menu_of_a_group_adds_the_clicked_transform()
    {
        var group = new TransformGroup();
        var shape = new RectShape { Transform = { Value = group } };
        using var history = new HistoryScope(shape);
        using var viewModel = new TransformEditorViewModel(
            new SimplePropertyAdapter<Transform?>((SimpleProperty<Transform?>)shape.Transform, shape));
        viewModel.Accept(ServiceVisitor.For(history.History));
        var editor = new TransformEditor { DataContext = viewModel };
        Window window = Show(editor);
        try
        {
            ClickAdd(editor, KnownTransformType.Rotation);

            Assert.That(group.Children.Single(), Is.TypeOf<RotationTransform>());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void The_add_menu_lists_the_ordinary_transforms_first()
    {
        var shape = new RectShape();
        using var history = new HistoryScope(shape);
        using TransformEditorViewModel viewModel = CreateViewModel(shape, history);
        var editor = new TransformEditor { DataContext = viewModel };
        Window window = Show(editor);
        try
        {
            editor.GetVisualDescendants().OfType<EffectEditorHeaderButtons>().Single()
                .RaiseEvent(new RoutedEventArgs(EffectEditorHeaderButtons.ClickEvent));
            HeadlessTestHelpers.Render();

            Assert.That(
                window.GetVisualDescendants().OfType<FAMenuFlyoutItem>().Select(item => item.Tag),
                Is.EqualTo(new object[]
                {
                    KnownTransformType.Translate,
                    KnownTransformType.Rotation,
                    KnownTransformType.Scale,
                    KnownTransformType.Skew,
                    KnownTransformType.Rotation3D,
                    KnownTransformType.Group,
                    KnownTransformType.Presenter,
                }));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(KnownTransformType.Translate, KnownTransformType.Rotation, KnownTransformType.Scale)]
    [TestCase(KnownTransformType.Scale, KnownTransformType.Rotation, KnownTransformType.Translate)]
    [TestCase(KnownTransformType.Rotation, KnownTransformType.Scale, KnownTransformType.Translate)]
    public void The_transforms_keep_their_order_whatever_order_they_are_added_in(
        KnownTransformType first, KnownTransformType second, KnownTransformType third)
    {
        var group = new TransformGroup();
        var shape = new RectShape { Transform = { Value = group } };
        using var history = new HistoryScope(shape);
        using TransformEditorViewModel viewModel = CreateViewModel(shape, history);

        viewModel.AddItem(first);
        viewModel.AddItem(second);
        viewModel.AddItem(third);

        Assert.That(
            group.Children.Select(child => child.GetType()),
            Is.EqualTo(new[] { typeof(TranslateTransform), typeof(RotationTransform), typeof(ScaleTransform) }));
    }

    [AvaloniaTest]
    public void The_add_button_of_a_single_transform_puts_it_in_a_group_and_undoes_in_one_step()
    {
        var existing = new TranslateTransform(30, 40);
        var shape = new RectShape { Transform = { Value = existing } };
        using var history = new HistoryScope(shape);
        using TransformEditorViewModel viewModel = CreateViewModel(shape, history);
        var editor = new TransformEditor { DataContext = viewModel };
        Window window = Show(editor);
        try
        {
            int before = history.History.UndoCount;
            ClickAdd(editor, KnownTransformType.Rotation);

            var group = (TransformGroup)shape.Transform.Value!;
            Assert.Multiple(() =>
            {
                Assert.That(group.Children, Has.Count.EqualTo(2));
                Assert.That(group.Children[0], Is.SameAs(existing));
                Assert.That(group.Children[1], Is.TypeOf<RotationTransform>());
                Assert.That(existing.HierarchicalParent, Is.SameAs(group));
                Assert.That(history.History.UndoCount, Is.EqualTo(before + 1));
            });

            Assert.That(history.History.Undo(), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(shape.Transform.Value, Is.SameAs(existing));
                Assert.That(existing.HierarchicalParent, Is.SameAs(shape));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void The_add_button_of_an_unset_transform_creates_a_group()
    {
        var shape = new RectShape { Transform = { Value = null } };
        using var history = new HistoryScope(shape);
        using TransformEditorViewModel viewModel = CreateViewModel(shape, history);
        var editor = new TransformEditor { DataContext = viewModel };
        Window window = Show(editor);
        try
        {
            ClickAdd(editor, KnownTransformType.Scale);

            Assert.That(((TransformGroup)shape.Transform.Value!).Children.Single(), Is.TypeOf<ScaleTransform>());
        }
        finally
        {
            window.Close();
        }
    }

    // A value that holds what could not be loaded is repaired by replacing or clearing it. Anything else keeps its
    // children: the add button adds and each row deletes itself.
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void The_right_click_menu_offers_changing_and_clearing_only_for_a_repair(bool unreadable)
    {
        var shape = new RectShape();
        if (unreadable)
        {
            shape.Transform.Value = new FallbackTransform();
        }
        else
        {
            ((TransformGroup)shape.Transform.Value!).Children.Add(new TranslateTransform());
        }

        using var history = new HistoryScope(shape);
        using TransformEditorViewModel viewModel = CreateViewModel(shape, history);
        var editor = new TransformEditor { DataContext = viewModel };
        Window window = Show(editor);
        try
        {
            ToggleButton toggle = editor.FindControl<ToggleButton>("expandToggle")!;
            var menu = (FAMenuFlyout)toggle.ContextFlyout!;
            menu.ShowAt(toggle);
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(
                    menu.Items.OfType<FAMenuFlyoutSubItem>().Single(item => item.Text == Strings.Change).IsVisible,
                    Is.EqualTo(unreadable));
                Assert.That(
                    menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Text == Strings.Null).IsVisible,
                    Is.EqualTo(unreadable));
                Assert.That(menu.Items.OfType<Control>().First(item => item.IsVisible), Is.Not.TypeOf<FAMenuFlyoutSeparator>());
                Assert.That(menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Text == Strings.Copy).IsVisible, Is.True);
            });
            menu.Hide();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void Adding_leaves_a_value_that_an_expression_decides_alone()
    {
        var target = new TranslateTransform(30, 40);
        var owner = new RectShape { Transform = { Value = target } };
        var shape = new RectShape();
        var stored = (TransformGroup)shape.Transform.Value!;
        shape.Transform.Expression = new ReferenceExpression<Transform?>(target.Id.Value);
        using var history = new HistoryScope(shape);
        using TransformEditorViewModel viewModel = CreateViewModel(shape, history);

        viewModel.AddItem(KnownTransformType.Rotation);

        Assert.Multiple(() =>
        {
            Assert.That(shape.Transform.Value, Is.SameAs(stored));
            Assert.That(stored.Children, Is.Empty);
            Assert.That(target.HierarchicalParent, Is.SameAs(owner));
        });
    }

    [AvaloniaTest]
    public void The_section_of_a_new_drawable_starts_closed_and_its_rows_open()
    {
        var shape = new RectShape();
        CanonicalTransformLayout.SeedEmptyGroup(shape);
        using var history = new HistoryScope(shape);
        using TransformEditorViewModel viewModel = CreateViewModel(shape, history);

        Assert.That(viewModel.IsExpanded.Value, Is.False);

        viewModel.IsExpanded.Value = true;

        Assert.That(
            viewModel.Group.Value!.Items.Select(item => ((TransformEditorViewModel)item.Context!).IsExpanded.Value),
            Is.EqualTo(new[] { true, true, true }));
    }

    [AvaloniaTest]
    public void A_row_that_holds_a_group_stays_closed()
    {
        var shape = new RectShape();
        ((TransformGroup)shape.Transform.Value!).Children.Add(new TransformGroup());
        using var history = new HistoryScope(shape);
        using TransformEditorViewModel viewModel = CreateViewModel(shape, history);

        viewModel.IsExpanded.Value = true;

        Assert.That(((TransformEditorViewModel)viewModel.Group.Value!.Items.Single().Context!).IsExpanded.Value, Is.False);
    }

    [AvaloniaTest]
    public void A_saved_state_overrides_the_open_row()
    {
        var shape = new RectShape();
        CanonicalTransformLayout.SeedEmptyGroup(shape);
        using var history = new HistoryScope(shape);
        using TransformEditorViewModel viewModel = CreateViewModel(shape, history);
        viewModel.IsExpanded.Value = true;

        viewModel.Group.Value!.Items[0].ReadFromJson(new JsonObject { ["IsExpanded"] = false });

        Assert.That(
            viewModel.Group.Value!.Items.Select(item => ((TransformEditorViewModel)item.Context!).IsExpanded.Value),
            Is.EqualTo(new[] { false, true, true }));
    }
}
