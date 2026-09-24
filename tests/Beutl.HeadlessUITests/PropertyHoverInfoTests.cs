using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Editors;
using Moq;
using Reactive.Bindings;
using TextBlock = Avalonia.Controls.TextBlock;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PropertyHoverInfoTests
{
    [Test]
    public void LegacyEditorInterfaceKeepsItsDescriptionTooltip()
    {
        INavigationButtonViewModel legacy = new LegacyNavigationButtonViewModel();

        Assert.That(legacy.HoverInfo, Is.SameAs(legacy.Description));
        Assert.That(legacy.HoverInfo.Value, Is.EqualTo("Legacy description"));
    }

    [AvaloniaTest]
    public void PropertyEditorHeadersShowHoverInfoWithoutChangingVisibleDescription()
    {
        var shape = new RectShape();
        using var model = new NumberEditorViewModel<float>(new EnginePropertyAdapter<float>(shape.Opacity, shape));
        model.Description.Value = "Opacity of the shape";
        var normal = new StringEditor();
        var settings = new StringEditor { EditorStyle = PropertyEditorStyle.Settings };
        model.Accept(normal);
        model.Accept(settings);
        var boolean = new BooleanEditor
        {
            Header = "Enabled",
            Description = "Enable rendering",
            HoverInfo = "Enable rendering\nType: bool"
        };
        var reference = new ReferenceEditor
        {
            Header = "Brush",
            Description = "Fill brush",
            HoverInfo = "Fill brush\nType: Brush"
        };
        var standalone = new StringEditor { Header = "Standalone", Description = "Plain help" };
        var window = new Window
        {
            Content = new StackPanel { Children = { normal, settings, boolean, reference, standalone } },
            Width = 520,
            Height = 380
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            string rich = model.HoverInfo.Value!;
            TextBlock normalHeader = normal.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Name == "PART_HeaderTextBlock");
            OptionsDisplayItem settingsItem = settings.GetVisualDescendants().OfType<OptionsDisplayItem>().Single();
            CheckBox booleanHeader = boolean.GetVisualDescendants().OfType<CheckBox>().Single();
            TextBlock referenceHeader = reference.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text == "Brush");
            TextBlock standaloneHeader = standalone.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Name == "PART_HeaderTextBlock");

            Assert.Multiple(() =>
            {
                Assert.That(ToolTip.GetTip(normalHeader), Is.EqualTo(rich));
                Assert.That(rich, Does.Contain("Opacity of the shape").And.Contain("float").And.Contain("100"));
                Assert.That(rich.Split(Environment.NewLine), Has.Length.EqualTo(3));
                Assert.That(settingsItem.Description, Is.EqualTo("Opacity of the shape"));
                Assert.That(ToolTip.GetTip(settingsItem), Is.EqualTo(rich));
                Assert.That(ToolTip.GetTip(booleanHeader), Is.EqualTo(boolean.HoverInfo));
                Assert.That(ToolTip.GetTip(referenceHeader), Is.EqualTo(reference.HoverInfo));
                Assert.That(ToolTip.GetTip(standaloneHeader), Is.EqualTo("Plain help"));
            });

            model.Description.Value = "Updated opacity description";
            HeadlessTestHelpers.Render(3);
            Assert.That(ToolTip.GetTip(normalHeader) as string, Does.Contain("Updated opacity description"));
            Assert.That(settingsItem.Description, Is.EqualTo("Updated opacity description"));

            Capture(window, "property-editors");
            ToolTip.SetIsOpen(normalHeader, true);
            HeadlessTestHelpers.Render(3);
            Capture(window, "property-editor-tooltip");
            ToolTip.SetIsOpen(normalHeader, false);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void NodePortLabelShowsSameTypeAndRange()
    {
        var graph = new GraphModel();
        var factory = new FactoryNode<RectShape>();
        graph.Nodes.Add(factory);
        var services = new Mock<IEditorContext>();
        services.Setup(x => x.GetService(typeof(IPropertyEditorFactory)))
            .Returns(new Mock<IPropertyEditorFactory>().Object);
        using var graphModel = new NodeGraphViewModel(graph, services.Object);
        InputPortViewModel opacity = graphModel.Nodes.Single().Items.OfType<InputPortViewModel>()
            .Single(item => item.Model?.Name == nameof(Drawable.Opacity));
        var row = new NodePortView { DataContext = opacity };
        var window = new Window { Content = row, Width = 300, Height = 90 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            TextBlock label = row.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text == opacity.Name.Value);
            string? tip = ToolTip.GetTip(label) as string;

            Assert.That(tip, Does.Contain("float").And.Contain("0").And.Contain("100"));
            Capture(window, "node-port-label");
            ToolTip.SetIsOpen(label, true);
            HeadlessTestHelpers.Render(3);
            Capture(window, "node-port-tooltip");
            ToolTip.SetIsOpen(label, false);
        }
        finally
        {
            row.DataContext = null;
            window.Close();
        }
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_PROPERTY_HOVER_CAPTURE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame();
        image?.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
    }

    private sealed class LegacyNavigationButtonViewModel : INavigationButtonViewModel
    {
        public string Header => "Legacy";
        public ReactivePropertySlim<string?> Description { get; } = new("Legacy description");
        public ReadOnlyReactivePropertySlim<bool> CanEdit => null!;
        public ReadOnlyReactivePropertySlim<bool> IsSet => null!;
        public ReadOnlyReactivePropertySlim<bool> IsNotSetAndCanWrite => null!;
        public bool CanWrite => false;
    }
}
