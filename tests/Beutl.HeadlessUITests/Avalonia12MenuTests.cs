using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Beutl.Editor.Components.PathEditorTab.Views;
using Beutl.Testing.Headless;
using Beutl.Views.Editors;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class Avalonia12MenuTests
{
    // These views declare flyouts outside the visual tree. Constructing the view alone
    // does not verify that the renamed menu items still acquire a presenter and theme.
    [AvaloniaTest]
    [TestCase(typeof(BrushEditor))]
    [TestCase(typeof(PropertyEditorMenu))]
    [TestCase(typeof(AudioEffectEditor))]
    [TestCase(typeof(CoreObjectEditor))]
    [TestCase(typeof(GeometryEditor))]
    [TestCase(typeof(TransformEditor))]
    [TestCase(typeof(DisplacementMapTransformEditor))]
    [TestCase(typeof(PathFigureListItemEditor))]
    [TestCase(typeof(PenEditor))]
    [TestCase(typeof(TextureSourceEditor))]
    [TestCase(typeof(FilterEffectEditor))]
    [TestCase(typeof(PathEditorTabView))]
    public void Editor_menus_open_with_their_items_and_labels(Type viewType)
    {
        var view = (Control)Activator.CreateInstance(viewType)!;
        var anchor = new Button { Content = "Menu" };
        var window = new Window
        {
            Content = new StackPanel { Children = { view, anchor } },
            Width = 640,
            Height = 480
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Control[] controls = view.GetLogicalDescendants().OfType<Control>()
                .Concat(view.GetVisualDescendants().OfType<Control>())
                .Prepend(view)
                .Distinct()
                .ToArray();
            FAMenuFlyout[] menus = controls
                .SelectMany(control => new[] { control.ContextFlyout, (control as Button)?.Flyout })
                .OfType<FAMenuFlyout>()
                .Distinct()
                .ToArray();
            Assert.That(menus, Is.Not.Empty, $"{viewType.Name} must expose its editing menus.");

            foreach (FAMenuFlyout menu in menus)
            {
                try
                {
                    menu.ShowAt(anchor);
                    HeadlessTestHelpers.Render();
                    Assert.That(menu.IsOpen, Is.True);
                    FAMenuFlyoutItem[] items = menu.Items.OfType<FAMenuFlyoutItem>()
                        .Where(item => item.IsVisible).ToArray();
                    Assert.That(items, Is.Not.Empty);
                    foreach (FAMenuFlyoutItem item in items)
                    {
                        Assert.Multiple(() =>
                        {
                            Assert.That(item.Text, Is.Not.Null.And.Not.Empty);
                            Assert.That(item.Bounds.Height, Is.GreaterThan(0), item.Text);
                            Assert.That(item.GetVisualDescendants().OfType<TextBlock>()
                                .Any(text => text.Text == item.Text), Is.True, item.Text);
                        });
                    }
                }
                finally
                {
                    menu.Hide();
                    HeadlessTestHelpers.Settle();
                }
            }
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
