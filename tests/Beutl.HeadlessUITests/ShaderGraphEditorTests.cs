using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.Language;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Shaders;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ShaderGraphEditorTests
{
    private const string BlurCode = """
        #version 450
        layout(location = 0) in vec2 uv;
        layout(location = 0) out vec4 color;
        layout(set = 0, binding = 0) uniform sampler2D source;
        layout(set = 0, binding = 1) uniform Parameters {
            float amount;
            vec2 direction;
            vec4 tint;
        } parameters;
        layout(push_constant) uniform Constants { float time; } constants;

        void main() {
            vec2 offset = parameters.direction * parameters.amount;
            color = (texture(source, uv - offset) + texture(source, uv + offset)) * 0.5 * parameters.tint;
        }
        """;

    private static IInputPort In(GraphNode node, string name)
        => (IInputPort)node.Items.Single(item => item is IInputPort && item.Name.Value == "In." + name);

    private static IOutputPort Out(GraphNode node, string name)
        => (IOutputPort)node.Items.Single(item => item is IOutputPort && item.Name.Value == "Out." + name);

    [AvaloniaTest]
    public async Task Resizing_a_shader_pass_gives_its_code_fields_room_and_reset_restores_the_type_default()
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("resize-shader-node");
        await TestShell.AddElementAsync(editor, () => new NodeGraphDrawable(), MediaTime.Zero, MediaTime.FromSeconds(1));
        GraphModel graph = ((NodeGraphDrawable)scene.Children.Single().Objects.Single()).Model.Value!;
        var value = new RandomSingleNode { Position = { Value = (20, 220) }, EditorSize = { Value = new(160) } };
        var pass = new ShaderRenderPassNode { Position = { Value = (240, 20) } };
        var output = new OutputNode { Position = { Value = (820, 50) } };
        graph.Nodes.AddRange([value, pass, output]);
        graph.Connect(pass.Padding, value.Value);
        graph.Connect(output.InputPort, Out(pass, "color"));
        editor.HistoryManager.Commit("Create shader graph");
        using var model = new NodeGraphTabViewModel(editor);
        model.Model.Value = graph;
        var view = new NodeGraphTabView { DataContext = model };
        var window = new Window { Content = view, Width = 900, Height = 820, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            await WaitForNodes(view);
            ZoomBorder zoom = view.GetVisualDescendants().OfType<ZoomBorder>().Single();
            zoom.EnableAnimations = false;
            zoom.SetMatrix(Matrix.CreateScale(0.75, 0.75), true);
            HeadlessTestHelpers.Render(2);
            string valueName = GraphNodeRegistry.FindItem(typeof(RandomSingleNode))!.DisplayName;
            TextBlock valueTitle = view.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == valueName);
            Assert.That(valueTitle.TextLayout.TextLines.Any(line => line.HasCollapsed), Is.False, "The narrow node keeps its name.");
            GraphNodeView node = view.GetVisualDescendants().OfType<GraphNodeView>()
                .Single(n => n.DataContext is GraphNodeViewModel vm && vm.GraphNode == pass);
            TextBox[] fields = [.. node.GetVisualDescendants().OfType<TextBox>().Where(box => box.AcceptsReturn)];
            Assert.That(fields, Has.Length.EqualTo(2));
            double[] natural = [.. fields.Select(box => box.Bounds.Height)];
            double height = node.Bounds.Height;
            int undo = editor.HistoryManager.UndoCount;

            DragCorner(100, 80);
            Assert.That(node.Bounds.Width, Is.EqualTo(480));
            Assert.That(node.Bounds.Height, Is.EqualTo(height + 80).Within(2));
            Assert.That(fields.Select(box => box.Bounds.Height), Is.All.EqualTo(natural.Average() + 40).Within(1));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
            HeadlessCapture.SaveIfRequested(view, "shader-graph", "resized-pass");

            DragCorner(-450, -node.Bounds.Height);
            Assert.That(node.Bounds.Width, Is.EqualTo(160));
            Assert.That(fields.Select(box => box.Bounds.Height), Is.All.EqualTo(32));
            HeadlessCapture.SaveIfRequested(view, "shader-graph", "minimum-pass");
            editor.HistoryManager.Undo();
            HeadlessTestHelpers.Render(2);
            Assert.That(node.Bounds.Width, Is.EqualTo(480));

            output.Position.Value = (820, 60);
            Border header = node.FindControl<Border>("handle")!;
            var menu = (FAMenuFlyout)header.ContextFlyout!;
            menu.ShowAt(header);
            HeadlessTestHelpers.Render(3);
            HeadlessTestHelpers.Click(menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Text == CommandNames.ResetNodeSize), move: true);
            Assert.That(node.Bounds.Width, Is.EqualTo(380));
            Assert.That(fields.Select(box => box.Bounds.Height), Is.EqualTo(natural));
            Assert.That(pass.EditorSize.Value, Is.EqualTo(default(NodeEditorSize)));
            editor.HistoryManager.Undo();
            HeadlessTestHelpers.Render(2);
            Assert.That(node.Bounds.Width, Is.EqualTo(480));
            Assert.That(output.Position.Value, Is.EqualTo((820d, 60d)), "Undoing the reset leaves the preceding edit in place.");

            var toggle = node.FindControl<ToggleButton>("expandToggle")!;
            HeadlessTestHelpers.Click(toggle, move: true);
            double collapsedHeight = node.Bounds.Height;
            DragCorner(40, 80);
            Assert.That(node.Bounds.Width, Is.EqualTo(520));
            Assert.That(node.Bounds.Height, Is.EqualTo(collapsedHeight));
            HeadlessTestHelpers.Click(toggle, move: true);
            Assert.That(fields.Select(box => box.Bounds.Height), Is.All.EqualTo(natural.Average() + 40).Within(1));

            void DragCorner(double dx, double dy)
            {
                var corner = new Point(node.Bounds.Width - 3, node.Bounds.Height - 3);
                Point start = node.TranslatePoint(corner, window)!.Value;
                Point end = node.TranslatePoint(corner + new Vector(dx, dy), window)!.Value;
                window.MouseMove(start);
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(end, RawInputModifiers.LeftMouseButton);
                window.MouseUp(end, MouseButton.Left);
                HeadlessTestHelpers.Render(3);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(600)]
    [TestCase(1200)]
    public async Task APassShowsWhatItsCodeDeclaresAndWhyItFailed(int width)
    {
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
        Window? window = null;
        try
        {
            (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("shader-editor");
            await TestShell.AddElementAsync(editor, () => new NodeGraphDrawable(), MediaTime.Zero, MediaTime.FromSeconds(1));
            GraphModel graph = ((NodeGraphDrawable)scene.Children.Single().Objects.Single()).Model.Value!;
            var pass = new ShaderFullscreenPassNode();
            var second = new ShaderFullscreenPassNode();
            var output = new OutputNode();
            graph.Nodes.Add(pass);
            graph.Nodes.Add(second);
            graph.Nodes.Add(output);
            pass.Code.Value = BlurCode;
            pass.Position.Value = (20, 20);
            second.Position.Value = (440, 20);
            output.Position.Value = (860, 20);
            graph.Connect(In(second, "source"), Out(pass, "color"));
            graph.Connect(output.InputPort, Out(second, "color"));
            editor.HistoryManager.Commit("Create shader graph");
            using var model = new NodeGraphTabViewModel(editor);
            model.Model.Value = graph;
            var view = new NodeGraphTabView { DataContext = model };
            window = new Window { Content = view, Width = width, Height = 900, RequestedThemeVariant = ThemeVariant.Dark };
            window.Show();
            HeadlessTestHelpers.Render(3);
            await WaitForNodes(view);

            // The ports carry the names in the code, and only value ports have editors.
            string?[] labels = [.. view.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text)];
            Assert.That(labels, Does.Contain("amount").And.Contain("direction").And.Contain("tint").And.Contain("time").And.Contain("source"));
            Assert.That(labels, Does.Contain("コード"));
            HeadlessCapture.SaveIfRequested(view, "shader-graph", $"pass-ja-{width}");

            // A wrong edit explains itself where it was made and leaves the wires alone.
            TextBox codeEditor = view.GetVisualDescendants().OfType<TextBox>()
                .First(text => text.AcceptsReturn && text.Text == BlurCode);
            Assert.That(codeEditor.Focus(), Is.True);
            codeEditor.SelectAll();
            window.KeyTextInput("invalid GLSL");
            HeadlessTestHelpers.Render(3);
            view.GetVisualDescendants().OfType<ZoomBorder>().Single().Focus();
            HeadlessTestHelpers.Render(3);
            Assert.That(pass.Code.Value, Is.EqualTo("invalid GLSL"));
            Assert.That(pass.Error.Value, Is.Not.Null.And.Not.Empty);
            Assert.That(In(pass, "parameters.amount"), Is.Not.Null);
            Assert.That(view.GetVisualDescendants().OfType<TextBlock>()
                .Any(text => text.Text != null && text.Text.Contains("Shader compilation failed")), Is.True);
            HeadlessCapture.SaveIfRequested(view, "shader-graph", $"error-ja-{width}");

            editor.HistoryManager.Undo();
            HeadlessTestHelpers.Render(3);
            Assert.That(pass.Code.Value, Is.EqualTo(BlurCode));
            Assert.That(codeEditor.Text, Is.EqualTo(BlurCode));
            Assert.That(pass.Error.Value, Is.Null);
        }
        finally
        {
            window?.Close();
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [AvaloniaTest]
    public async Task ALoopOpensOnItsStateAndIterationInfo()
    {
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
        Window? window = null;
        try
        {
            (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("shader-loop");
            await TestShell.AddElementAsync(editor, () => new NodeGraphDrawable(), MediaTime.Zero, MediaTime.FromSeconds(1));
            GraphModel graph = ((NodeGraphDrawable)scene.Children.Single().Objects.Single()).Model.Value!;
            var replay = new ShaderReplayNode();
            graph.Nodes.Add(replay);
            replay.Position.Value = (20, 20);
            editor.HistoryManager.Commit("Create a loop");
            using var model = new NodeGraphTabViewModel(editor);
            model.Model.Value = graph;
            var view = new NodeGraphTabView { DataContext = model };
            window = new Window { Content = view, Width = 1200, Height = 720, RequestedThemeVariant = ThemeVariant.Dark };
            window.Show();
            HeadlessTestHelpers.Render(3);
            await WaitForNodes(view);
            HeadlessCapture.SaveIfRequested(view, "shader-graph", "replay-ja");
            string?[] outer = [.. view.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text)];
            Assert.That(outer, Does.Contain("毎秒の更新回数").And.Contain("開始時刻 (秒)"));

            model.NavigateTo(replay.Group.Value);
            HeadlessTestHelpers.Render(3);
            await WaitForNodes(view);
            string?[] inner = [.. view.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text)];
            // Port labels only: a node's title keeps the language the app started in.
            Assert.That(inner, Does.Contain("反復番号").And.Contain("経過時間").And.Contain("画像"));
            HeadlessCapture.SaveIfRequested(view, "shader-graph", "replay-inside-ja");
        }
        finally
        {
            window?.Close();
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    private static Task WaitForNodes(NodeGraphTabView view) => HeadlessTestHelpers.WaitUntilAsync(() =>
    {
        HeadlessTestHelpers.Render();
        return view.GetVisualDescendants().OfType<GraphNodeView>().All(node => node.Opacity >= 0.99);
    }, TimeSpan.FromSeconds(3));
}
