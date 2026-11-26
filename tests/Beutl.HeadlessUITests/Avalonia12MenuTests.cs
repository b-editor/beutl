using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Beutl.Editor.Components.GraphEditorTab.Views;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.Editor.Components.PathEditorTab.Views;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Language;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class Avalonia12MenuTests
{
    [AvaloniaTest]
    public async Task Player_context_flyout_resets_zoom_and_stays_closed_in_camera_mode()
    {
        using var graph = await GraphEditorContextMenuTests.GraphScope.CreateAsync();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 640, Height = 480 };
        var actions = new List<PreviewZoomAction>();
        using var subscription = editor.Player.PreviewZoomRequested.Subscribe(actions.Add);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Control anchor = view.FindControl<Panel>("framePanel")!;
            var flyout = (FAMenuFlyout)anchor.ContextFlyout!;
            editor.Player.IsCameraMode.Value = true;
            flyout.ShowAt(anchor);
            Assert.That(flyout.IsOpen, Is.False);
            editor.Player.IsCameraMode.Value = false;
            editor.Player.FrameMatrix.Value = Beutl.Graphics.Matrix.CreateScale(2, 2);
            flyout.ShowAt(anchor);
            HeadlessTestHelpers.Render();
            HeadlessCapture.SaveIfRequested(flyout.Popup.Child!, nameof(Avalonia12MenuTests), "preview-menu");
            FAMenuFlyoutItem fit = flyout.Items.OfType<FAMenuFlyoutItem>()
                .Single(item => item.Text == Strings.PreviewZoomFit);
            Assert.That(fit.InputGesture, Is.EqualTo(new KeyGesture(Key.D0, KeyModifiers.Alt)));
            Assert.That(flyout.Items.OfType<FAMenuFlyoutItem>()
                .Single(item => item.Text == Strings.SaveSelectedElementAsImage).IsEnabled, Is.False);
            HeadlessTestHelpers.Click(fit, move: true);
            Assert.That(actions, Is.EqualTo(new[] { PreviewZoomAction.Fit }));
            Assert.That(editor.Player.FrameMatrix.Value, Is.EqualTo(Beutl.Graphics.Matrix.Identity));
            Assert.That(flyout.IsOpen, Is.False);
        }
        finally
        {
            editor.Player.IsCameraMode.Value = false;
            view.DataContext = null;
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

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
    [TestCase(typeof(GraphEditorView))]
    [TestCase(typeof(NodeGraphView))]
    [TestCase(typeof(InlineAnimationLayer))]
    [TestCase(typeof(GraphModelNodeMemberView))]
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
