using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Beutl.Controls;
using Beutl.Editor.Components.ObjectPropertyTab.ViewModels;
using Beutl.Editor.Components.ObjectPropertyTab.Views;
using Beutl.Editor.Models;
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
    [TestCase(320, false)]
    [TestCase(640, false)]
    [TestCase(320, true)]
    [TestCase(640, true)]
    public async Task Back_bar_keeps_property_navigation_and_empty_state_working(int width, bool light)
    {
        await TestReset.ResetShellAsync();
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"object-property-bar-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "object-property-bar", root))!;
        Scene scene = project.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await adder.AddAsync([new ElementDescription(TimeSpan.Zero, TimeSpan.FromSeconds(2), 0,
            new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None);
        Element element = scene.Children.First();
        var first = element.Objects[0];
        var second = new EllipseShape();
        ((IElementObjectService)editor.GetService(typeof(IElementObjectService))!).Add(element, second);
        var selection = (IEditorSelection)editor.GetService(typeof(IEditorSelection))!;
        selection.SelectedObject.Value = null;
        using var model = new ObjectPropertyTabViewModel(editor);
        using var history = new HistoryViewModel(editor);
        var referenceView = new HistoryView { DataContext = history };
        var view = new ObjectPropertyTabView { DataContext = model };
        var window = new Window
        {
            Content = referenceView,
            Width = width,
            Height = 500,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
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
            Capture("empty");

            selection.SelectedObject.Value = first;
            HeadlessTestHelpers.Render();
            Assert.That(model.ChildContext.Value!.Target, Is.SameAs(first));
            Assert.That(back.IsEffectivelyEnabled, Is.True);
            Assert.That(properties.Items, Is.Not.Empty);
            Assert.That(properties.DataContext, Is.SameAs(model.ChildContext.Value));
            CheckBar();
            Capture("selected");

            selection.SelectedObject.Value = second;
            HeadlessTestHelpers.Render();
            Assert.That(model.ChildContext.Value!.Target, Is.SameAs(second));
            Point center = back.TranslatePoint(new Point(back.Bounds.Width / 2, back.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            HeadlessTestHelpers.Render();
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

        void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("BEUTL_OBJECT_PROPERTY_BAR_CAPTURE") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var image = window.CaptureRenderedFrame();
            image?.Save(Path.Combine(directory, $"{name}-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
        }
    }
}
