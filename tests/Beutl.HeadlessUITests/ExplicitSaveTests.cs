using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Generative;
using Beutl.NodeGraph.Nodes.Generative;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// An explicit save writes the elements edited since the last one (b-editor/beutl#3915). What it leaves out must be what
// it would have written, so each test ends by comparing the file with the element.
[TestFixture, NonParallelizable]
public sealed class ExplicitSaveTests
{
    private const string Mark = " ";

    private bool _autoSaveWasSuppressed;

    // Left to itself, an auto-save would write what the commit before each save changed.
    [SetUp]
    public void SuppressAutoSave()
    {
        _autoSaveWasSuppressed = EditViewModel.IsAutoSaveSuppressedForTesting;
        EditViewModel.IsAutoSaveSuppressedForTesting = true;
    }

    [TearDown]
    public void RestoreAutoSave() => EditViewModel.IsAutoSaveSuppressedForTesting = _autoSaveWasSuppressed;

    private sealed record Clip(Element Element, RectShape Shape)
    {
        public float Opacity(int index) => KeyFrames[index].Value.Value;

        public KeyFrameAnimation<float> Animation => (KeyFrameAnimation<float>)Shape.Opacity.GetKeyFrameAnimation()!;

        public IList<KeyFrame<float>> KeyFrames => [.. Animation.KeyFrames.Cast<KeyFrame<float>>()];
    }

    private static readonly (string Name, Action<Clip> Edit)[] s_edits =
    [
        ("the element's name", clip => clip.Element.Name.Value = "edited"),
        ("the element's place", clip => clip.Element.Start.Value += MediaTime.FromSeconds(100)),
        ("a value of an object", clip => clip.Shape.Width.Value = 123),
        ("an object's nested object", clip => clip.Shape.Fill.Value = new SolidColorBrush { Color = { Value = Colors.Blue } }),
        ("a value inside a nested object", clip => ((SolidColorBrush)clip.Shape.Fill.Value!).Color.Value = Colors.Blue),
        ("an expression", clip => clip.Shape.Width.Expression = Expression.Create<float>("1 + 1")),
        ("an added key frame", clip => clip.Animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.FromSeconds(2) },
            Value = { Value = 10 },
        })),
        ("a key frame's value", clip => clip.KeyFrames[0].Value.Value = 42),
        ("a key frame's time", clip => clip.KeyFrames[0].KeyTime.Value = MediaTime.FromSeconds(3)),
        ("a key frame's curve", clip => ((SplineEasing)clip.KeyFrames[0].Easing.Value!).X1 = 0.75f),
        ("an added object", clip => clip.Element.Objects.Add(new EllipseShape())),
        ("a removed object", clip => clip.Element.Objects.Remove(clip.Shape)),
        ("an effect", clip => clip.Shape.FilterEffect.Value = new Blur()),
        ("a value of an effect", clip => ((Blur)clip.Shape.FilterEffect.Value!).Sigma.Value = new Size(5, 5)),
    ];

    private static IEnumerable<string> EditNames => s_edits.Select(edit => edit.Name);

    [AvaloniaTest]
    [TestCaseSource(nameof(EditNames))]
    public async Task A_save_writes_what_an_edit_changed_and_leaves_the_other_files(string name)
    {
        (Scene scene, EditViewModel editor, Clip edited, Element untouched) = await OpenAsync();

        await SaveAsync(editor);
        MarkFile(untouched);
        s_edits.Single(edit => edit.Name == name).Edit(edited);
        await SaveAsync(editor);

        using (Assert.EnterMultipleScope())
        {
            AssertFileHolds(edited.Element);
            Assert.That(File.ReadAllText(untouched.Uri!.LocalPath), Does.EndWith(Mark), "The save read or wrote the other file.");
        }
    }

    [AvaloniaTest]
    public async Task A_save_writes_an_edit_that_has_not_been_committed()
    {
        (Scene scene, EditViewModel editor, Clip edited, Element untouched) = await OpenAsync();
        await SaveAsync(editor);

        edited.Shape.Width.Value = 123;
        Assert.That(await editor.SaveAsync(), Is.True);

        AssertFileHolds(edited.Element);
    }

    // A settled request changes its key outside history. Reopening with the old key would get the old failure back.
    [AvaloniaTest]
    public async Task A_save_writes_the_request_key_a_settled_failure_renewed()
    {
        (Scene scene, EditViewModel editor, Clip edited, Element untouched) = await OpenAsync();
        var node = new AiImageGenerationNode();
        node.Prompt.Property!.SetValue("a cat");
        var graph = new NodeGraphDrawable();
        graph.Model.Value!.Nodes.Add(node);
        edited.Element.Objects.Add(graph);
        HeadlessTestHelpers.Settle();
        await SaveAsync(editor);
        MarkFile(untouched);
        string before = node.RequestKeySeed.Value;

        await new GenerativeGraphRunner(new SettledFailureExecutor(), new EditorGenerativeRunHost(editor, null))
            .RunAsync(graph.Model.Value!, null, force: false, CancellationToken.None);
        Assert.That(node.RequestKeySeed.Value, Is.Not.EqualTo(before));
        await SaveAsync(editor);

        using (Assert.EnterMultipleScope())
        {
            AssertFileHolds(edited.Element);
            Assert.That(File.ReadAllText(untouched.Uri!.LocalPath), Does.EndWith(Mark));
        }
    }

    private sealed class SettledFailureExecutor : IGenerativeNodeExecutor
    {
        public Task<GenerativeExecutionResult> ExecuteAsync(
            GenerativeRequest request,
            IProgress<GenerativeProgress> progress,
            CancellationToken cancellationToken)
            => throw new GenerativeExecutionException("refused") { SettledRequest = true };
    }

    [AvaloniaTest]
    public async Task A_save_writes_what_undo_and_redo_changed()
    {
        (Scene scene, EditViewModel editor, Clip edited, Element untouched) = await OpenAsync();
        await SaveAsync(editor);
        edited.Shape.Width.Value = 123;
        await SaveAsync(editor);

        Assert.That(editor.HistoryManager.Undo(), Is.True);
        await SaveAsync(editor);
        AssertFileHolds(edited.Element);

        Assert.That(editor.HistoryManager.Redo(), Is.True);
        await SaveAsync(editor);
        AssertFileHolds(edited.Element);
    }

    [AvaloniaTest]
    public async Task A_save_writes_an_element_added_since_the_last_one()
    {
        (Scene scene, EditViewModel editor, Clip edited, Element untouched) = await OpenAsync();
        await SaveAsync(editor);

        Clip added = AddClip(scene);
        await SaveAsync(editor);

        AssertFileHolds(added.Element);
    }

    // It also brings files from an older format up to date.
    [AvaloniaTest]
    public async Task The_first_save_writes_every_element()
    {
        (Scene scene, EditViewModel editor, Clip edited, Element untouched) = await OpenAsync();
        File.WriteAllText(untouched.Uri!.LocalPath, "{}");

        await SaveAsync(editor);

        AssertFileHolds(untouched);
    }

    [AvaloniaTest]
    public async Task A_file_that_has_gone_is_written_again()
    {
        (Scene scene, EditViewModel editor, Clip edited, Element untouched) = await OpenAsync();
        await SaveAsync(editor);
        File.Delete(untouched.Uri!.LocalPath);

        await SaveAsync(editor);

        AssertFileHolds(untouched);
    }

    // A relink changes a source in place, which no operation reports.
    [AvaloniaTest]
    public async Task A_save_with_media_repairs_writes_every_element()
    {
        (Scene scene, EditViewModel editor, Clip edited, Element untouched) = await OpenAsync();
        await SaveAsync(editor);
        MarkFile(untouched);
        editor.HasMediaRepairs.Value = true;

        await SaveAsync(editor);

        Assert.That(File.ReadAllText(untouched.Uri!.LocalPath), Does.Not.EndWith(Mark));
    }

    [AvaloniaTest]
    public async Task The_save_after_one_with_media_repairs_goes_back_to_the_edited_elements()
    {
        (Scene scene, EditViewModel editor, Clip edited, Element untouched) = await OpenAsync();
        editor.HasMediaRepairs.Value = true;
        await SaveAsync(editor);
        MarkFile(untouched);

        await SaveAsync(editor);

        Assert.That(File.ReadAllText(untouched.Uri!.LocalPath), Does.EndWith(Mark));
    }

    private static async Task<(Scene Scene, EditViewModel Editor, Clip Edited, Element Untouched)> OpenAsync()
    {
        await TestReset.ResetShellAsync();
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync($"explicit-save-{Guid.NewGuid():N}");
        Clip edited = AddClip(scene);
        edited.Shape.Fill.Value = new SolidColorBrush { Color = { Value = Colors.Red } };
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.FromSeconds(1) },
            Value = { Value = 50 },
            Easing = { Value = new SplineEasing(0.25f, 0.25f, 0.5f, 0.5f) },
        });
        edited.Shape.Opacity.SetKeyFrameAnimation(animation);
        edited.Shape.FilterEffect.Value = new Blur();
        Clip untouched = AddClip(scene);
        return (scene, editor, edited, untouched.Element);
    }

    private static Clip AddClip(Scene scene)
    {
        var shape = new RectShape();
        var element = new Element
        {
            Start = { Value = MediaTime.FromSeconds(scene.Children.Count * 10) },
            Length = { Value = MediaTime.FromSeconds(5) },
        };
        element.Objects.Add(shape);
        element.Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, $"{Guid.NewGuid():N}.belm"));
        scene.Children.Add(element);
        HeadlessTestHelpers.Settle();
        return new Clip(element, shape);
    }

    private static async Task SaveAsync(EditViewModel editor)
    {
        editor.HistoryManager.Commit();
        Assert.That(await editor.SaveAsync(), Is.True);
    }

    // Whitespace reads the same but any write removes it, so it stays only in a file the save left alone.
    private static void MarkFile(Element element) => File.AppendAllText(element.Uri!.LocalPath, Mark);

    private static void AssertFileHolds(Element element)
    {
        JsonObject expected = CoreSerializer.SerializeToJsonObject(element, new CoreSerializerOptions
        {
            BaseUri = element.Uri,
            Mode = CoreSerializationMode.Write,
        });
        var saved = (JsonObject)JsonNode.Parse(File.ReadAllText(element.Uri!.LocalPath))!;
        // Added to a stored file only, and about the app rather than the element.
        saved.Remove("minAppVersion");
        Assert.That(JsonNode.DeepEquals(expected, saved), Is.True, () => $"Expected {expected}\nbut the file holds {saved}");
    }
}
