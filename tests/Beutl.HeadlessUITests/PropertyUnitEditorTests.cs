using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Primitives;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

public class PropertyUnitEditorTests
{
    [AvaloniaTest]
    [TestCase("scale", "%", 100f, "75.5", 75.5f)]
    [TestCase("rotation", "°", 0f, "45.5", 45.5f)]
    [TestCase("width", null, 100f, "123.5", 123.5f)]
    [TestCase("fov", "°", 60f, "45.5", 45.5f)]
    public async Task Scalar_editors_label_units_and_keep_the_original_numeric_values(string kind, string? symbol, float initial, string text, float expected)
    {
        await TestReset.ResetShellAsync();
        var (adapter, owner) = kind switch
        {
            "scale" => Adapt(new ScaleTransform(), value => value.Scale),
            "rotation" => Adapt(new RotationTransform(), value => value.Rotation),
            "fov" => Adapt(new PerspectiveCamera(), value => value.FieldOfView),
            _ => Adapt(new RectShape(), value => value.Width)
        };
        using var history = new PropertyEditorTestHost.HistoryScope(owner);
        var (model, control) = PropertyEditorTestHost.CreateControl(adapter);
        using var disposable = model;
        model.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
        var number = (NumberEditor<float>)control;
        var next = new Button { Content = "Next" };
        var window = new Window { Content = new StackPanel { Children = { control, next } }, Width = 360, Height = 160 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            Assert.That(number.Header, Is.EqualTo(symbol is null ? adapter.DisplayName : $"{adapter.DisplayName} ({symbol})"));
            Assert.That(number.Value, Is.EqualTo(initial));
            TypeValue(window, number.GetVisualDescendants().OfType<TextBox>().Single(), next, text);
            Assert.That(adapter.GetValue(), Is.EqualTo(expected));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task Vector_units_label_the_whole_value_without_rescaling_components()
    {
        await TestReset.ResetShellAsync();
        var cube = new Cube3D();
        var adapter = new AnimatablePropertyAdapter<Vector3>((AnimatableProperty<Vector3>)cube.Scale, cube);
        using var history = new PropertyEditorTestHost.HistoryScope(cube);
        var (model, control) = PropertyEditorTestHost.CreateControl(adapter);
        using var disposable = model;
        model.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
        var vector = (Vector3Editor<float>)control;
        var next = new Button { Content = "Next" };
        var window = new Window { Content = new StackPanel { Children = { control, next } }, Width = 360, Height = 180 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            Assert.That(vector.Header, Is.EqualTo($"{adapter.DisplayName} (×)"));
            TypeValue(window, vector.GetVisualDescendants().OfType<TextBox>().First(), next, "1.25");
            Assert.That(cube.Scale.Value, Is.EqualTo(new Vector3(1.25f, 1f, 1f)));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(typeof(ScaleMatrixNode), "×", "1.25", 1.25f)]
    [TestCase(typeof(RotationMatrixNode), "°", "45.5", 45.5f)]
    public async Task Matrix_node_editors_use_input_unit_metadata(Type nodeType, string symbol, string text, float expected)
    {
        await TestReset.ResetShellAsync();
        var editor = await TestShell.OpenNewSceneEditorAsync("node-unit");
        var node = (GraphNode)Activator.CreateInstance(nodeType)!;
        var source = new RandomSingleNode();
        var graph = new GraphModel { Nodes = { node, source } };
        var input = node is ScaleMatrixNode scale ? scale.Scale : ((RotationMatrixNode)node).Rotation;
        using var vm = new NodeGraphViewModel(graph, editor);
        var view = new GraphNodeView { DataContext = vm.Nodes.Single(model => model.GraphNode == node) };
        var next = new Button { Content = "Next" };
        var window = new Window { Content = new StackPanel { Children = { view, next } }, Width = 360, Height = 480 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            NumberEditor<float>[] numbers = view.GetVisualDescendants().OfType<NumberEditor<float>>().ToArray();
            Assert.That(numbers, Is.Not.Empty);
            Assert.That(numbers.Select(number => number.Header), Has.All.EndsWith($"({symbol})"));
            TypeValue(window, numbers[0].GetVisualDescendants().OfType<TextBox>().Single(), next, text);
            Assert.That(input.Property!.GetValue(), Is.EqualTo(expected));
            input.Display.Value = new DisplayAttribute { Name = "Renamed input" };
            HeadlessTestHelpers.Render(3);
            Assert.That(numbers[0].Header, Is.EqualTo($"Renamed input ({symbol})"));

            var port = view.GetVisualDescendants().OfType<NodePortView>()
                .Single(port => port.DataContext is InputPortViewModel model && model.Model == input);
            var connection = graph.Connect(input, source.Value);
            HeadlessTestHelpers.Render(3);
            Assert.That(port.GetVisualDescendants().OfType<NumberEditor<float>>(), Is.Empty);
            var label = port.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Single();
            Assert.That(label.Text, Is.EqualTo($"Renamed input ({symbol})"));

            input.Display.Value = new DisplayAttribute { Name = "Connected input" };
            HeadlessTestHelpers.Render(3);
            Assert.That(label.Text, Is.EqualTo($"Connected input ({symbol})"));

            graph.Disconnect(connection);
            HeadlessTestHelpers.Render(3);
            Assert.That(port.GetVisualDescendants().OfType<NumberEditor<float>>().Single(), Is.SameAs(numbers[0]));
            Assert.That(numbers[0].Header, Is.EqualTo($"Connected input ({symbol})"));
        }
        finally
        {
            view.DataContext = null;
            window.Close();
        }
    }

    private static (IPropertyAdapter<float> Adapter, EngineObject Owner) Adapt<T>(T owner, Func<T, IExpressionProperty<float>> property) where T : EngineObject
        => (new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)property(owner), owner), owner);

    private static void TypeValue(Window window, TextBox box, Button next, string text)
    {
        Assert.That(box.Focus(), Is.True);
        box.SelectAll();
        window.KeyTextInput(text);
        next.Focus();
        HeadlessTestHelpers.Render(3);
    }
}
