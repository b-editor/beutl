using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Engine;
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
using Beutl.Services;
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

    // Holds up the save that first reads this object once it is armed, for the test to do what a user can do meanwhile.
    [SuppressResourceClassGeneration]
    private sealed class HeldWhileSaving : EngineObject
    {
        private bool _armed;
        private int _failedReads;

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ManualResetEventSlim Release { get; } = new();

        public int? Thread { get; private set; }

        // Fails this many reads, as an edit on the thread that edits can make a read on a worker fail.
        public void Arm(int failedReads)
        {
            _failedReads = failedReads;
            _armed = true;
        }

        public override void Serialize(ICoreSerializationContext context)
        {
            base.Serialize(context);
            if (!_armed) return;

            if (Thread is null)
            {
                Thread = Environment.CurrentManagedThreadId;
                Reached.TrySetResult();
                Assert.That(Release.Wait(TimeSpan.FromSeconds(30)), Is.True);
            }

            if (_failedReads-- > 0) throw new InvalidOperationException("Collection was modified.");
        }
    }

    private static HeldWhileSaving AddHeldClip(Scene scene, out Clip clip)
    {
        clip = AddClip(scene);
        var held = new HeldWhileSaving();
        clip.Element.Objects.Add(held);
        HeadlessTestHelpers.Settle();
        return held;
    }

    private static async Task<Task<bool>> StartSaveAsync(EditViewModel editor, HeldWhileSaving held, int failedReads = 0)
    {
        editor.HistoryManager.Commit();
        held.Arm(failedReads);
        Task<bool> save = editor.SaveAsync().AsTask();
        await held.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
        return save;
    }

    private static async Task<Exception?> FailureOf(Task save)
    {
        try
        {
            await save;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [AvaloniaTest]
    public async Task A_save_leaves_the_thread_that_edits_free_while_it_writes_the_elements()
    {
        (Scene scene, EditViewModel editor, _, _) = await OpenAsync();
        HeldWhileSaving held = AddHeldClip(scene, out _);

        Task<bool> save = await StartSaveAsync(editor, held);
        // Getting here at all, with the save still on the held element, shows that this thread is free.
        bool dispatched = await Dispatcher.UIThread.InvokeAsync(() => true);
        bool wasRunning = !save.IsCompleted;
        held.Release.Set();

        Assert.That(await save, Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dispatched, Is.True);
            Assert.That(wasRunning, Is.True);
            Assert.That(held.Thread, Is.Not.EqualTo(Environment.CurrentManagedThreadId));
        }
    }

    [AvaloniaTest]
    public async Task An_edit_during_a_save_reaches_the_files_with_the_other_edits()
    {
        (Scene scene, EditViewModel editor, Clip edited, Element untouched) = await OpenAsync();
        HeldWhileSaving held = AddHeldClip(scene, out Clip heldClip);
        await SaveAsync(editor);
        MarkFile(untouched);
        edited.Shape.Width.Value = 1;
        heldClip.Shape.Width.Value = 1;

        Task<bool> save = await StartSaveAsync(editor, held);
        // An element written or about to be, the one being read, and one the save had not heard of.
        edited.Shape.Width.Value = 2;
        edited.Element.Name.Value = "during the save";
        heldClip.Shape.Width.Value = 3;
        Clip added = AddClip(scene);
        held.Release.Set();
        Assert.That(await save, Is.True);

        using (Assert.EnterMultipleScope())
        {
            AssertFileHolds(edited.Element);
            AssertFileHolds(heldClip.Element);
            AssertFileHolds(added.Element);
            Assert.That(File.ReadAllText(untouched.Uri!.LocalPath), Does.EndWith(Mark));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "*.tmp"), Is.Empty);
        }
    }

    [AvaloniaTest]
    public async Task An_edit_taken_back_during_a_save_leaves_the_file_as_it_was()
    {
        (Scene scene, EditViewModel editor, Clip edited, _) = await OpenAsync();
        HeldWhileSaving held = AddHeldClip(scene, out Clip heldClip);
        await SaveAsync(editor);
        float width = edited.Shape.Width.Value;
        edited.Shape.Width.Value = width + 1;
        heldClip.Shape.Width.Value = 1;

        Task<bool> save = await StartSaveAsync(editor, held);
        edited.Shape.Width.Value = width;
        held.Release.Set();

        Assert.That(await save, Is.True);
        AssertFileHolds(edited.Element);
    }

    [AvaloniaTest]
    public async Task An_edit_once_the_elements_are_written_waits_for_the_next_save()
    {
        (Scene scene, EditViewModel editor, Clip edited, _) = await OpenAsync();
        await SaveAsync(editor);
        edited.Shape.Width.Value = 1;
        editor.HistoryManager.Commit();
        var moving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        Task<bool> save;
        using (StorageWriteTransaction.InjectFaultsForTesting((step, _) =>
               {
                   if (step != StorageWriteStep.Replace || moving.Task.IsCompleted) return;
                   moving.TrySetResult();
                   Assert.That(release.Wait(TimeSpan.FromSeconds(30)), Is.True);
               }))
        {
            save = editor.SaveAsync().AsTask();
        }

        await moving.Task.WaitAsync(TimeSpan.FromSeconds(30));
        JsonObject written = Serialized(edited.Element);
        edited.Shape.Width.Value = 2;
        release.Set();
        Assert.That(await save, Is.True);
        Assert.That(JsonNode.DeepEquals(written, Stored(edited.Element)), Is.True);

        await SaveAsync(editor);

        AssertFileHolds(edited.Element);
    }

    [AvaloniaTest]
    public async Task An_element_that_could_not_be_read_on_a_worker_is_written_all_the_same()
    {
        (Scene scene, EditViewModel editor, _, _) = await OpenAsync();
        HeldWhileSaving held = AddHeldClip(scene, out Clip clip);
        clip.Shape.Width.Value = 1;

        Task<bool> save = await StartSaveAsync(editor, held, failedReads: 1);
        held.Release.Set();

        Assert.That(await save, Is.True);
        AssertFileHolds(clip.Element);
    }

    [AvaloniaTest]
    public async Task An_element_that_cannot_be_read_fails_the_save()
    {
        (Scene scene, EditViewModel editor, _, _) = await OpenAsync();
        HeldWhileSaving held = AddHeldClip(scene, out _);

        Task<bool> save = await StartSaveAsync(editor, held, failedReads: 2);
        held.Release.Set();

        Assert.That(await FailureOf(save), Is.TypeOf<InvalidOperationException>());
    }

    // What is written again on the thread that edits belongs to the same transaction as the rest.
    [AvaloniaTest]
    public async Task A_failed_save_takes_back_an_edit_made_during_it_and_the_next_save_writes_it()
    {
        (Scene scene, EditViewModel editor, Clip edited, Element untouched) = await OpenAsync();
        HeldWhileSaving held = AddHeldClip(scene, out Clip heldClip);
        await SaveAsync(editor);
        JsonObject before = Stored(edited.Element);
        string sceneFile = scene.Uri!.LocalPath;
        heldClip.Shape.Width.Value = 1;
        // The scene file is written last, and only if it changes.
        scene.Duration.Value += MediaTime.FromSeconds(1);

        using (StorageWriteTransaction.InjectFaultsForTesting((step, path) =>
               {
                   if (step == StorageWriteStep.Replace && path == sceneFile) throw new IOException("Injected.");
               }))
        {
            Task<bool> save = await StartSaveAsync(editor, held);
            edited.Shape.Width.Value = 2;
            held.Release.Set();
            Assert.That(await FailureOf(save), Is.Not.Null);
        }

        Assert.That(JsonNode.DeepEquals(before, Stored(edited.Element)), Is.True);

        Assert.That(await editor.SaveAsync(), Is.True);
        using (Assert.EnterMultipleScope())
        {
            AssertFileHolds(edited.Element);
            AssertFileHolds(heldClip.Element);
        }
    }

    // Saving all writes every scene of the project too, so editing stops for it as it does for a lifecycle save.
    [AvaloniaTest]
    public async Task Save_all_writes_the_project_off_the_thread_that_edits()
    {
        (Scene scene, EditViewModel editor, _, _) = await OpenAsync();
        HeldWhileSaving held = AddHeldClip(scene, out Clip clip);
        editor.HistoryManager.Commit();
        held.Arm(failedReads: 0);

        Task saveAll = TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();
        await held.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
        bool enabled = editor.IsEnabled.Value;
        held.Release.Set();
        await saveAll;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(enabled, Is.False);
            Assert.That(editor.IsEnabled.Value, Is.True);
            Assert.That(held.Thread, Is.Not.EqualTo(Environment.CurrentManagedThreadId));
            AssertFileHolds(clip.Element);
        }
    }

    [AvaloniaTest]
    public async Task Save_all_keeps_an_item_added_to_the_project_during_it()
    {
        (Scene scene, EditViewModel editor, _, _) = await OpenAsync();
        Project project = scene.FindHierarchicalParent<Project>()!;
        HeldWhileSaving held = AddHeldClip(scene, out _);
        editor.HistoryManager.Commit();
        held.Arm(failedReads: 0);
        using var notifications = new NotificationCapture();

        Task saveAll = TestShell.MainViewModel.MenuBar.SaveAll.ExecuteAsync();
        await held.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
        string addedFile = Path.Combine(Path.GetDirectoryName(project.Uri!.LocalPath)!, "added", "added.scene");
        var added = new Scene(1920, 1080, "added") { Uri = new Uri(addedFile) };
        ProjectPersistence.AddItemAndPersist(project, added);
        held.Release.Set();
        await saveAll;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notifications.All.Where(item => item.Type == NotificationType.Error), Is.Empty);
            Assert.That(File.ReadAllText(project.Uri!.LocalPath), Does.Contain("added.scene"));
        }
    }

    // A version-control cycle saves without the write lease a tab's close waits for.
    [AvaloniaTest]
    public async Task A_save_finishes_when_its_editor_closes_during_it()
    {
        (Scene scene, EditViewModel editor, _, _) = await OpenAsync();
        HeldWhileSaving held = AddHeldClip(scene, out Clip clip);

        Task<bool> save = await StartSaveAsync(editor, held);
        await TestShell.Editor.CloseTabItem(TestShell.Editor.SelectedTabItem.Value!, saveChanges: false);
        held.Release.Set();

        Assert.That(await save, Is.True);
        AssertFileHolds(clip.Element);
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

    private static JsonObject Serialized(Element element) => CoreSerializer.SerializeToJsonObject(element, new CoreSerializerOptions
    {
        BaseUri = element.Uri,
        Mode = CoreSerializationMode.Write,
    });

    private static JsonObject Stored(Element element)
    {
        var saved = (JsonObject)JsonNode.Parse(File.ReadAllText(element.Uri!.LocalPath))!;
        // Added to a stored file only, and about the app rather than the element.
        saved.Remove("minAppVersion");
        return saved;
    }

    private static void AssertFileHolds(Element element)
    {
        JsonObject expected = Serialized(element);
        JsonObject saved = Stored(element);
        Assert.That(JsonNode.DeepEquals(expected, saved), Is.True, () => $"Expected {expected}\nbut the file holds {saved}");
    }
}
