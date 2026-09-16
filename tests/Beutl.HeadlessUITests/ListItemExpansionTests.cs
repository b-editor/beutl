using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Services.Adapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Editors;
using Beutl.Views.Editors;
using FluentIcons.Avalonia.Fluent;
using Moq;
using Reactive.Bindings;
using ScaleTransform = Beutl.Graphics.Transformation.ScaleTransform;
using TransformGroup = Beutl.Graphics.Transformation.TransformGroup;
using TranslateTransform = Beutl.Graphics.Transformation.TranslateTransform;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ListItemExpansionTests
{
    [AvaloniaTest]
    [TestCase(280, false)]
    [TestCase(400, false)]
    [TestCase(760, false)]
    [TestCase(280, true)]
    [TestCase(400, true)]
    [TestCase(760, true)]
    public async Task List_headers_keep_original_expansion_without_checked_grip_background(int width, bool light)
    {
        var blur = new Blur { Sigma = { CurrentValue = new Beutl.Graphics.Size(8, 8) } };
        var shadow = new DropShadow
        {
            Position = { CurrentValue = new Beutl.Graphics.Point(12, 12) },
            Sigma = { CurrentValue = new Beutl.Graphics.Size(16, 16) },
            Color = { CurrentValue = Beutl.Media.Color.FromArgb(160, 20, 30, 60) }
        };
        var effects = new FilterEffectGroup { Children = { blur, shadow } };
        var transforms = new TransformGroup
        {
            Children = { new TranslateTransform(24, 12), new ScaleTransform(120, 80) }
        };
        var card = new RoundedRectShape
        {
            Width = { CurrentValue = 640 },
            Height = { CurrentValue = 220 },
            Fill = { CurrentValue = new Beutl.Media.SolidColorBrush(Beutl.Media.Color.FromRgb(255, 158, 68)) },
            Transform = { CurrentValue = transforms },
            FilterEffect = { CurrentValue = effects }
        };
        var element = new Element
        {
            Uri = new Uri(Path.Combine(BeutlHomeIsolation.CurrentHome!, $"list-expansion-{Guid.NewGuid():N}.belm")),
            Objects = { card }
        };
        using var history = new HistoryManager(element, new OperationSequenceGenerator());
        using var selected = new ReactivePropertySlim<CoreObject?>(null);
        var selection = new Mock<IEditorSelection>();
        selection.SetupGet(s => s.SelectedObject).Returns(selected);
        var extensions = TestShell.Extensions;
        var factory = new PropertyEditorFactoryAdapter(extensions);
        var host = new Mock<IEditorContext>();
        host.Setup(h => h.GetService(It.IsAny<Type>())).Returns((Type type) =>
            type == typeof(IEditorSelection) ? selection.Object :
            type == typeof(IPropertyEditorFactory) ? factory :
            type == typeof(ExtensionProvider) ? extensions :
            type == typeof(HistoryManager) ? history : null);
        using var model = new ElementPropertyTabViewModel(host.Object);
        var view = new ElementPropertyTabView { DataContext = model };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 1050,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
        };
        try
        {
            selected.Value = element;
            window.Show();
            foreach (var context in Flatten(model.Items.Single().Properties))
            {
                if (context is FilterEffectEditorViewModel filter) filter.IsExpanded.Value = true;
                if (context is TransformEditorViewModel transform) transform.IsExpanded.Value = true;
            }
            await Settle();
            var filterRows = view.GetVisualDescendants().OfType<FilterEffectListItemEditor>().ToArray();
            var transformRows = view.GetVisualDescendants().OfType<TransformListItemEditor>().ToArray();
            Assert.That(filterRows, Has.Length.EqualTo(2));
            Assert.That(transformRows, Has.Length.EqualTo(2));
            var filterHeader = (ToggleButton)filterRows[0].ReorderHandle!;
            var transformHeader = (ToggleButton)transformRows[0].ReorderHandle!;
            foreach (var header in filterRows.Select(r => (ToggleButton)r.ReorderHandle!)
                         .Concat(transformRows.Select(r => (ToggleButton)r.ReorderHandle!)))
                AssertState(header, false);
            Assert.That(filterHeader.GetVisualDescendants().OfType<FluentIcon>()
                .Any(c => c.IsEffectivelyVisible && c.Icon.ToString() == "ReOrderDotsVertical"), Is.True);
            window.MouseMove(default);
            await Settle();
            var closedBackground = GripBackground(filterHeader);
            var closedTransformBackground = GripBackground(transformHeader);
            Capture("collapsed");

            window.MouseMove(Center(Grip(filterHeader)));
            await Settle();
            var closedHoverBackground = GripBackground(filterHeader);
            Assert.That(closedHoverBackground.Color.A, Is.GreaterThan(0));
            Capture("hover-collapsed", keepPointer: true);
            Click(Grip(filterHeader));
            await Settle();
            AssertState(filterHeader, true);
            Assert.That(GripBackground(filterHeader), Is.EqualTo(closedHoverBackground));
            Capture("hover-expanded", keepPointer: true);

            Click(Grip(transformHeader));
            window.MouseMove(default);
            await Settle();
            AssertState(filterHeader, true);
            AssertState(transformHeader, true);
            Assert.That(((FilterEffectEditorViewModel)filterRows[0].DataContext!).IsExpanded.Value, Is.True);
            Assert.That(((TransformEditorViewModel)transformRows[0].DataContext!).IsExpanded.Value, Is.True);
            Assert.That(filterRows[0].FindControl<Panel>("content")!.IsVisible, Is.True);
            Assert.That(GripBackground(filterHeader), Is.EqualTo(closedBackground));
            Assert.That(GripBackground(transformHeader), Is.EqualTo(closedTransformBackground));
            Assert.That(view.FindControl<ScrollViewer>("scrollViewer")!.HorizontalScrollBarVisibility,
                Is.EqualTo(ScrollBarVisibility.Disabled));
            if (width >= 640)
            {
                var numbers = view.GetVisualDescendants().OfType<NumberEditor<float>>()
                    .Where(editor => editor.IsEffectivelyVisible).ToArray();
                Assert.That(numbers.Length, Is.GreaterThanOrEqualTo(4));
                Assert.That(numbers.Any(editor => editor.GetVisualAncestors().Contains(transformRows[0])), Is.True);
                foreach (var editor in numbers)
                {
                    var grid = editor.GetVisualDescendants().OfType<PropertyEditorGrid>().Single();
                    Assert.That(grid.ColumnDefinitions[0].Width.IsAbsolute, Is.True, editor.Header);
                    var input = editor.GetVisualDescendants().OfType<TextBox>().Single();
                    Assert.That(input.TranslatePoint(default, view)!.Value.X,
                        Is.EqualTo(view.Bounds.Width / 2).Within(1), editor.Header);
                }
            }
            Capture("expanded");

            Click(Title(filterHeader));
            Click(Title(transformHeader));
            await Settle();
            AssertState(filterHeader, false);
            AssertState(transformHeader, false);
            Assert.That(filterRows[0].FindControl<Panel>("content")!.IsVisible, Is.False);
            Assert.That(filterHeader.Focus(NavigationMethod.Tab), Is.True);
            PressSpace();
            await Settle();
            AssertState(filterHeader, true);
            PressSpace();
            await Settle();
            AssertState(filterHeader, false);

            var visibility = filterRows[0].GetVisualDescendants().OfType<ToggleButton>()
                .Single(b => b.IsEffectivelyVisible && b != filterHeader);
            Click(visibility);
            Assert.That(blur.IsEnabled, Is.False);
            AssertState(filterHeader, false);
            Click(visibility);
            Assert.That(blur.IsEnabled, Is.True);

            var otherHeader = (ToggleButton)filterRows[1].ReorderHandle!;
            Point start = Center(Grip(filterHeader));
            Point end = Center(Grip(otherHeader));
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(0, 8), RawInputModifiers.LeftMouseButton);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);
            await Settle();
            Assert.That(effects.Children[0], Is.SameAs(shadow));
            Assert.That(effects.Children[1], Is.SameAs(blur));
            // Reordering rematerializes item views; continue with their current controls.
            filterRows = view.GetVisualDescendants().OfType<FilterEffectListItemEditor>().ToArray();
            filterHeader = (ToggleButton)filterRows.Single(r =>
                ((FilterEffectEditorViewModel)r.DataContext!).Value.Value == blur).ReorderHandle!;
            otherHeader = (ToggleButton)filterRows.Single(r =>
                ((FilterEffectEditorViewModel)r.DataContext!).Value.Value == shadow).ReorderHandle!;
            AssertState(filterHeader, false);
            AssertState(otherHeader, false);
            Capture("reordered");

            var delete = ((Panel)filterHeader.Parent!).Children.OfType<Button>()
                .Single(b => b is not ToggleButton);
            Click(delete);
            Assert.That(effects.Children, Has.Count.EqualTo(1));
            Assert.That(effects.Children[0], Is.SameAs(shadow));
            AssertState((ToggleButton)view.GetVisualDescendants().OfType<FilterEffectListItemEditor>()
                .Single().ReorderHandle!, false);
        }
        finally { window.Close(); }

        async Task Settle()
        {
            await Task.Delay(350);
            HeadlessTestHelpers.Render(3);
        }
        Point Center(Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        void Click(Control control)
        {
            Point point = Center(control);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            HeadlessTestHelpers.Render(3);
        }
        void PressSpace()
        {
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            HeadlessTestHelpers.Render(3);
        }
        void Capture(string state, bool keepPointer = false)
        {
            if (Environment.GetEnvironmentVariable("BEUTL_LIST_EXPANSION_CAPTURE") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            if (!keepPointer) window.MouseMove(default);
            HeadlessTestHelpers.Render(3);
            using var frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
            frame!.Save(Path.Combine(directory, $"{state}-{width}-{(light ? "light" : "dark")}.png"), PngBitmapEncoderOptions.Default);
        }
    }

    private static ContentPresenter Title(ToggleButton header) => header.GetVisualDescendants().OfType<ContentPresenter>()
        .Single(c => c.Name == "ContentPresenter");
    private static ContentPresenter Grip(ToggleButton header) => header.GetVisualDescendants().OfType<ContentPresenter>()
        .Single(c => c.Name == "TagContentPresenter");
    private static void AssertState(ToggleButton header, bool expanded)
    {
        Assert.That(header.IsChecked, Is.EqualTo(expanded));
        Assert.That(Grip(header).IsEffectivelyVisible, Is.True);
        Assert.That(header.GetVisualDescendants().OfType<Control>()
            .Any(c => c.Name is "PART_ExpandCollapseChevron" or "PART_ExpandCollapseBorder"), Is.False);
    }
    private static (Color Color, double Opacity) GripBackground(ToggleButton header)
    {
        return Grip(header).Background is ISolidColorBrush { Color.A: > 0, Opacity: > 0 } brush
            ? (brush.Color, brush.Opacity) : default;
    }
    private static IEnumerable<IPropertyEditorContext> Flatten(IEnumerable<IPropertyEditorContext?> contexts)
    {
        foreach (var context in contexts)
        {
            if (context is null) continue;
            yield return context;
            if (context is PropertyEditorGroupContext group)
                foreach (var nested in Flatten(group.Properties)) yield return nested;
        }
    }
}
