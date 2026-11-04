using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// Finding out what the editors hold walks the whole project, so a selection must not cost one walk per file (#3904).
[TestFixture]
[NonParallelizable]
public sealed class EditorFileUsageTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("beutl-file-usage-").FullName;
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [AvaloniaTest]
    public async Task A_file_an_editor_holds_is_found_among_free_ones()
    {
        EditViewModel editor = (await TestShell.OpenNewSceneAsync("file-usage-among-free", 320, 180)).Editor;
        IEditorFileUsage usage = editor.GetRequiredService<IEditorFileUsage>();
        string scene = editor.Scene.Uri!.LocalPath;
        string project = Path.GetDirectoryName(scene)!;
        string media = Path.Combine(project, "media.png");
        File.WriteAllBytes(media, [0]);
        var source = new ImageSource();
        source.ReadFrom(new Uri(media));
        var element = new Element();
        element.Objects.Add(new SourceImage { Source = { Value = source } });
        editor.Scene.Children.Add(element);
        string free = Path.Combine(_directory, "free.png");

        Assert.Multiple(() =>
        {
            Assert.That(usage.IsAnyFileInUse([]), Is.False);
            Assert.That(usage.IsAnyFileInUse([(free, false), (_directory, true)]), Is.False);
            Assert.That(usage.IsAnyFileInUse([(free, false), (scene, false)]), Is.True, "the document");
            Assert.That(usage.IsAnyFileInUse([(free, false), (media, false)]), Is.True, "a source");
            Assert.That(usage.IsAnyFileInUse([(_directory, true), (project, true)]), Is.True, "a folder");
            Assert.That(usage.IsFileInUse(free, false), Is.False);
            Assert.That(usage.IsFileInUse(media, false), Is.True);
            Assert.That(usage.IsFileInUse(project, true), Is.True);
        });
    }

    [AvaloniaTest]
    public async Task A_move_of_several_files_asks_about_them_together()
    {
        var usage = new RecordingUsage(true);
        (string, bool)[] files = [(Touch("a.txt"), false), (Touch("b.txt"), false), (_directory, true)];
        using var context = new Context(usage);
        using var browser = new FileBrowserTabViewModel(context);
        using var notifications = new NotificationCapture();

        await browser.TransferFilesToDirectoryAsync(files, Path.Combine(_directory, "target"), move: true);

        Assert.Multiple(() =>
        {
            Assert.That(usage.Asked, Is.EqualTo(new[] { files }));
            Assert.That(notifications.Single().Message, Is.EqualTo(MessageStrings.FileInUse));
        });
    }

    // Each of the two checks of a delete, before and after the dialog, stands for all the files.
    [AvaloniaTest]
    public async Task A_delete_of_several_files_keeps_them_all_if_one_comes_into_use_during_the_dialog()
    {
        string first = Touch("a.txt");
        string second = Touch("b.txt");
        var usage = new RecordingUsage(false, true);
        using var context = new Context(usage);
        using var browser = new FileBrowserTabViewModel(context);
        browser.ConfirmAsync = _ => Task.FromResult(FAContentDialogResult.Primary);
        using var firstItem = new FileSystemItemViewModel(first, isDirectory: false);
        using var secondItem = new FileSystemItemViewModel(second, isDirectory: false);
        using var notifications = new NotificationCapture();

        await browser.DeleteItemsAsync([firstItem, secondItem]);

        Assert.Multiple(() =>
        {
            Assert.That(usage.Asked, Has.Count.EqualTo(2));
            Assert.That(usage.Asked, Is.All.EqualTo(new[] { (first, false), (second, false) }));
            Assert.That(File.Exists(first), Is.True);
            Assert.That(File.Exists(second), Is.True);
            Assert.That(notifications.Single().Message, Is.EqualTo(MessageStrings.FileInUse));
        });
    }

    private string Touch(string name)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, [0]);
        return path;
    }

    // Answers in the order given, and records a question about one file as a question of its own.
    private sealed class RecordingUsage(params bool[] answers) : IEditorFileUsage
    {
        private readonly Queue<bool> _answers = new(answers);

        public List<(string, bool)[]> Asked { get; } = [];

        public bool IsFileInUse(string path, bool isDirectory) => IsAnyFileInUse([(path, isDirectory)]);

        public bool IsAnyFileInUse(IReadOnlyCollection<(string Path, bool IsDirectory)> files)
        {
            Asked.Add([.. files]);
            return _answers.Dequeue();
        }
    }

    private sealed class Context(IEditorFileUsage usage) : IEditorContext
    {
        public CoreObject Object { get; } = new Document();

        public EditorExtension Extension => null!;

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

        public T? FindToolTab<T>(Func<T, bool> condition) where T : IToolContext => default;

        public T? FindToolTab<T>() where T : IToolContext => default;

        public bool OpenToolTab(IToolContext item) => false;

        public void CloseToolTab(IToolContext item)
        {
        }

        public object? GetService(Type serviceType) => serviceType == typeof(IEditorFileUsage) ? usage : null;

        public void Dispose() => IsEnabled.Dispose();

        private sealed class Document : CoreObject;
    }
}
