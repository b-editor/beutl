using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Shaders;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

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
