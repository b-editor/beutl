using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Beutl.Controls;
using Beutl.Editor.Components.ObjectPropertyTab.ViewModels;
using Beutl.Editor.Components.ObjectPropertyTab.Views;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ObjectPropertyToolBarTests
{
    [AvaloniaTest]
    [TestCase(320)]
    [TestCase(640)]
    public async Task Back_bar_keeps_property_navigation_and_empty_state_working(int width)
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("object-property-bar");
        await TestShell.AddElementAsync(editor, () => new RectShape(), TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element element = scene.Children.First();
        var first = element.Objects[0];
        var second = new EllipseShape();
        ((IElementObjectService)editor.GetService(typeof(IElementObjectService))!).Add(element, second);
        var selection = (IEditorSelection)editor.GetService(typeof(IEditorSelection))!;
        selection.SelectedObject.Value = null;
        using var model = new ObjectPropertyTabViewModel(editor);
        // The editor turns the property tabs it has open to the selection.
        Assert.That(editor.OpenToolTab(model), Is.True);
        using var history = new HistoryViewModel(editor);
        var referenceView = new HistoryView { DataContext = history };
        var view = new ObjectPropertyTabView { DataContext = model };
        var window = new Window
        {
            Content = referenceView,
            Width = width,
            Height = 500,
            RequestedThemeVariant = ThemeVariant.Dark
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            ToolTabBar referenceBar = referenceView.FindControl<ToolTabBar>("ToolBar")!;
            double referenceLeftInset = referenceView.FindControl<Button>("UndoButton")!
                .TranslatePoint(default, referenceBar)!.Value.X;
            double referenceHeight = referenceBar.Bounds.Height;
            window.Content = view;
            HeadlessTestHelpers.Render();
            ToolTabBar bar = view.FindControl<ToolTabBar>("ToolBar")!;
            Button back = view.FindControl<Button>("BackButton")!;
            ItemsControl properties = view.FindControl<ItemsControl>("PropertyList")!;
            Assert.That(back.IsEffectivelyEnabled, Is.False);
            Assert.That(properties.Items, Is.Empty);
            CheckBar();

            selection.SelectedObject.Value = first;
            HeadlessTestHelpers.Render();
            Assert.That(model.ChildContext.Value!.Target, Is.SameAs(first));
            Assert.That(back.IsEffectivelyEnabled, Is.True);
            Assert.That(properties.Items, Is.Not.Empty);
            Assert.That(properties.DataContext, Is.SameAs(model.ChildContext.Value));
            CheckBar();

            selection.SelectedObject.Value = second;
            HeadlessTestHelpers.Render();
            Assert.That(model.ChildContext.Value!.Target, Is.SameAs(second));
            HeadlessTestHelpers.Click(back);
            Assert.That(model.ChildContext.Value!.Target, Is.SameAs(first));
            Assert.That(properties.DataContext, Is.SameAs(model.ChildContext.Value));

            Assert.That(back.Focus(), Is.True);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            HeadlessTestHelpers.Render();
            Assert.That(model.ChildContext.Value, Is.Null);
            Assert.That(back.IsEffectivelyEnabled, Is.False);
            Assert.That(properties.Items, Is.Empty);
            CheckBar();

            void CheckBar()
            {
                Assert.Multiple(() =>
                {
                    Assert.That(bar.Bounds.Height, Is.EqualTo(referenceHeight));
                    Assert.That(back.TranslatePoint(default, bar)!.Value.X, Is.EqualTo(referenceLeftInset));
                    Assert.That(back.Command, Is.Not.Null);
                    Assert.That(ToolTip.GetTip(back), Is.EqualTo(Strings.Back));
                    Assert.That(AutomationProperties.GetName(back), Is.EqualTo(Strings.Back));
                    Assert.That(properties.TranslatePoint(default, view)!.Value.Y,
                        Is.GreaterThanOrEqualTo(bar.TranslatePoint(default, view)!.Value.Y + bar.Bounds.Height));
                });
            }
        }
        finally { window.Close(); }
    }
}
