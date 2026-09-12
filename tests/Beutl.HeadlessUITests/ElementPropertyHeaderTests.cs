using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using TextBlock = Avalonia.Controls.TextBlock;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ElementPropertyHeaderTests
{
    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(640, false)]
    [TestCase(320, true)]
    [TestCase(640, true)]
    public async Task HeadersMatchToolBarsAndPreserveExpansionVisibilityAndReordering(int width, bool light)
    {
        await TestReset.ResetShellAsync();
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"element-headers-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "element-headers", root))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await adder.AddAsync([new ElementDescription(TimeSpan.Zero, TimeSpan.FromSeconds(2), 0,
            new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None);
        Element element = scene.Children.First();
        var firstObject = element.Objects[0];
        var secondObject = new EllipseShape();
        ((IElementObjectService)editor.GetService(typeof(IElementObjectService))!).Add(element, secondObject);
        ((IEditorSelection)editor.GetService(typeof(IEditorSelection))!).SelectedObject.Value = element;
        using var model = new ElementPropertyTabViewModel(editor);
        foreach (EngineObjectPropertyViewModel item in model.Items) item.IsExpanded.Value = false;
        var view = new ElementPropertyTabView { DataContext = model };
        var referenceBar = new ToolTabBar { Content = new TextBlock { Text = Strings.ElementProperty } };
        var window = new Window
        {
            Content = referenceBar,
            SizeToContent = SizeToContent.Height,
            Width = width,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            double referenceHeight = referenceBar.Bounds.Height;
            double referenceRightInset = referenceBar.Padding.Right;
            var referenceStroke = ((Avalonia.Media.ISolidColorBrush)referenceBar.BorderBrush!).Color;
            window.SizeToContent = SizeToContent.Manual;
            window.Height = 500;
            window.Content = view;
            HeadlessTestHelpers.Render();

            EngineObjectPropertyView[] entries = view.GetVisualDescendants().OfType<EngineObjectPropertyView>().ToArray();
            Assert.That(entries, Has.Length.EqualTo(2));
            var first = entries[0];
            Expander expander = first.GetLogicalDescendants().OfType<Expander>().First();
            ToggleButton header = expander.GetVisualDescendants().OfType<ToggleButton>().First(b => b.Name == "ExpanderHeader");
            TextBlock title = first.FindControl<TextBlock>("headerText")!;
            ToggleButton visibility = first.FindControl<ToggleButton>("VisibilityButton")!;
            Border headerBorder = header.GetVisualChildren().OfType<Border>().Single();
            Border chevron = header.GetVisualDescendants().OfType<Border>().First(b => b.Name == "ExpandCollapseChevronBorder");
            Assert.That(header.Bounds.Height, Is.EqualTo(referenceHeight));
            Assert.That(header.Bounds.Width - chevron.TranslatePoint(default, header)!.Value.X - chevron.Bounds.Width,
                Is.EqualTo(referenceRightInset));
            Assert.That(((Avalonia.Media.ISolidColorBrush)headerBorder.Background!).Color.A, Is.Zero);
            Capture("collapsed");

            Click(title);
            Assert.That(model.Items[0].IsExpanded.Value, Is.True);
            HeadlessTestHelpers.Render();
            Assert.That(header.Bounds.Height, Is.EqualTo(referenceHeight));
            Assert.That(((Avalonia.Media.ISolidColorBrush)headerBorder.BorderBrush!).Color, Is.EqualTo(referenceStroke));
            Capture("expanded");
            Click(title);
            Assert.That(model.Items[0].IsExpanded.Value, Is.False);
            Assert.That(header.Focus(), Is.True);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            HeadlessTestHelpers.Settle();
            Assert.That(model.Items[0].IsExpanded.Value, Is.True);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            HeadlessTestHelpers.Settle();
            Assert.That(model.Items[0].IsExpanded.Value, Is.False);
            Click(visibility);
            Assert.Multiple(() =>
            {
                Assert.That(firstObject.IsEnabled, Is.False);
                Assert.That(model.Items[0].IsExpanded.Value, Is.False,
                    "the visibility button must not toggle expansion");
            });
            Click(visibility);
            Assert.That(firstObject.IsEnabled, Is.True);

            string originalTitle = title.Text!;
            title.Text = string.Join(" ", Enumerable.Repeat(originalTitle, 20));
            HeadlessTestHelpers.Render();
            Assert.That(header.Bounds.Height, Is.GreaterThan(referenceHeight));
            Point titlePosition = title.TranslatePoint(default, header)!.Value;
            Assert.That(titlePosition.Y + title.Bounds.Height, Is.LessThanOrEqualTo(header.Bounds.Height));
            Capture("long-title");
            title.Text = originalTitle;
            HeadlessTestHelpers.Render();

            ContextMenu menu = first.FindControl<Grid>("headerPanel")!.ContextMenu!;
            Click(title, MouseButton.Right);
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(menu.Items.OfType<MenuItem>().Select(item => item.Header),
                Does.Contain(Strings.Remove).And.Contain(Strings.SaveAsTemplate));
            menu.Close();
            HeadlessTestHelpers.Settle();

            Border grip = first.FindControl<Border>("dragBorder")!;
            Point start = Center(grip);
            Point end = Center(entries[1].FindControl<Border>("dragBorder")!);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(0, 8), RawInputModifiers.LeftMouseButton);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);
            HeadlessTestHelpers.Render();
            Assert.That(element.Objects[0], Is.SameAs(secondObject));
            Assert.That(element.Objects[1], Is.SameAs(firstObject));
            Assert.That(model.Items.All(item => !item.IsExpanded.Value), Is.True);
            Capture("reordered");
        }
        finally { window.Close(); }

        Point Center(Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

        void Click(Control control, MouseButton button = MouseButton.Left)
        {
            Point center = Center(control);
            window.MouseDown(center, button);
            window.MouseUp(center, button);
            HeadlessTestHelpers.Settle();
        }

        void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("BEUTL_ELEMENT_HEADER_CAPTURE") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var image = window.CaptureRenderedFrame();
            image?.Save(Path.Combine(directory, $"{name}-{width}-{light}.png"), PngBitmapEncoderOptions.Default);
        }
    }
}
