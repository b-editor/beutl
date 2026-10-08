using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Animation;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.IO;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.ViewModels.Editors;
using Beutl.Views.Editors;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class FileSourceEditorTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("file-source-editor-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [AvaloniaTest]
    [TestCase(EditorSurface.Property)]
    [TestCase(EditorSurface.Node)]
    [TestCase(EditorSurface.Settings)]
    [TestCase(EditorSurface.ListItem)]
    public void Custom_media_source_uses_the_file_picker_in_every_context(EditorSurface surface)
    {
        var holder = new FilePropertyHolder();
        var adapter = new EnginePropertyAdapter<TestMediaSource?>(holder.Source, holder);
        var (vm, control) = CreateControl(adapter, surface);
        using var _ = vm;

        Assert.That(vm, Is.TypeOf<FileSourceEditorViewModel<TestMediaSource>>());
        Assert.That(control, Is.TypeOf<StorageFileEditor>());
        var editor = (StorageFileEditor)control;
        Assert.That(editor.OpenOptions.FileTypeFilter!.Single().Name, Is.EqualTo("Photoshop documents"));
        Assert.That(editor.OpenOptions.FileTypeFilter!.Single().Patterns, Is.EqualTo(new[] { "*.psd", "*.psb" }));

        using var history = new HistoryScope(holder);
        vm.Accept(new Services(history.History));
        Confirm(editor, CreateFile("image.psd"));
        Assert.That(history.History.UndoCount, Is.EqualTo(1), "One confirmation must be handled once, including in lists.");
        Assert.That(holder.Source.CurrentValue!.Uri.LocalPath, Is.EqualTo(editor.Value!.FullName));
    }

    [AvaloniaTest]
    public void Non_core_object_file_source_is_selectable_without_a_filter()
    {
        var holder = new FilePropertyHolder();
        var (vm, control) = CreateControl(new EnginePropertyAdapter<TestFileSource?>(holder.PlainSource, holder));
        using var _ = vm;
        using var history = new HistoryScope(holder);
        vm.Accept(new Services(history.History));
        var editor = (StorageFileEditor)control;
        string path = CreateFile("plain.txt");

        Confirm(editor, path);

        Assert.Multiple(() =>
        {
            Assert.That(holder.PlainSource.CurrentValue!.Uri.LocalPath, Is.EqualTo(path));
            Assert.That(editor.Value!.FullName, Is.EqualTo(path));
            Assert.That(editor.OpenOptions.FileTypeFilter, Is.Null.Or.Empty);
        });
    }

    [AvaloniaTest]
    public void File_info_properties_accept_multiple_filters_and_platform_metadata()
    {
        var holder = new FilePropertyHolder();
        var (vm, control) = CreateControl(new EnginePropertyAdapter<FileInfo?>(holder.File, holder));
        using var _ = vm;
        var filters = ((StorageFileEditor)control).OpenOptions.FileTypeFilter!;

        Assert.Multiple(() =>
        {
            Assert.That(filters.Select(x => x.Name), Is.EqualTo(new[] { "Subtitles", "Text files" }));
            Assert.That(filters[0].Patterns, Is.EqualTo(new[] { "*.srt" }));
            Assert.That(filters[0].MimeTypes, Is.EqualTo(new[] { "application/x-subrip" }));
            Assert.That(filters[1].AppleUniformTypeIdentifiers, Is.EqualTo(new[] { "public.plain-text" }));
        });
    }

    [AvaloniaTest]
    public void Built_in_source_editors_keep_their_behavior_and_support_filter_overrides()
    {
        var holder = new FilePropertyHolder();
        var (imageVm, imageControl) = CreateControl(new EnginePropertyAdapter<ImageSource?>(holder.Image, holder));
        using var _ = imageVm;
        var (cubeVm, cubeControl) = CreateControl(new EnginePropertyAdapter<CubeSource?>(holder.Cube, holder));
        using var __ = cubeVm;

        Assert.That(imageControl, Is.TypeOf<ImageSourceEditor>());
        Assert.That(cubeControl, Is.TypeOf<CubeSourceEditor>());
        Assert.That(((ImageSourceEditor)imageControl).FindControl<StorageFileEditor>("FileEditor")!
            .OpenOptions.FileTypeFilter!.Single().Patterns, Is.EqualTo(new[] { "*.png" }));
        Assert.That(((CubeSourceEditor)cubeControl).FindControl<StorageFileEditor>("FileEditor")!
            .OpenOptions.FileTypeFilter!.Single().Patterns, Is.EqualTo(new[] { "*.cube" }));
    }

    [AvaloniaTest]
    public void Selection_replacement_and_clearing_are_undoable_without_mutating_the_previous_source()
    {
        var holder = new FilePropertyHolder();
        var (vm, control) = CreateControl(new EnginePropertyAdapter<TestMediaSource?>(holder.Source, holder));
        using var _ = vm;
        using var history = new HistoryScope(holder);
        vm.Accept(new Services(history.History));
        var editor = (StorageFileEditor)control;
        string firstPath = CreateFile("first.psd");
        string secondPath = CreateFile("second.psb");

        Confirm(editor, firstPath);
        TestMediaSource first = holder.Source.CurrentValue!;
        Assert.That(history.History.Undo(), Is.True);
        Assert.That(holder.Source.CurrentValue, Is.Null);
        Assert.That(editor.Value, Is.Null);
        Assert.That(history.History.Redo(), Is.True);
        Assert.That(holder.Source.CurrentValue, Is.SameAs(first));
        Assert.That(editor.Value!.FullName, Is.EqualTo(firstPath));

        Confirm(editor, secondPath);
        TestMediaSource second = holder.Source.CurrentValue!;
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(first.Uri.LocalPath, Is.EqualTo(firstPath));
        Assert.That(history.History.Undo(), Is.True);
        Assert.That(holder.Source.CurrentValue, Is.SameAs(first));
        Assert.That(history.History.Redo(), Is.True);
        Assert.That(holder.Source.CurrentValue, Is.SameAs(second));

        Confirm(editor, null);
        Assert.That(holder.Source.CurrentValue, Is.Null);
        Assert.That(history.History.Undo(), Is.True);
        Assert.That(holder.Source.CurrentValue, Is.SameAs(second));
        Assert.That(editor.Value!.FullName, Is.EqualTo(secondPath));
        Assert.That(history.History.Redo(), Is.True);
        Assert.That(editor.Value, Is.Null);
    }

    [AvaloniaTest]
    public void Selection_updates_the_editing_keyframe_and_preserves_the_static_value()
    {
        var holder = new FilePropertyHolder();
        var staticSource = new TestMediaSource();
        staticSource.ReadFrom(new Uri(CreateFile("static.psd")));
        holder.Source.CurrentValue = staticSource;
        var keyframe = new KeyFrame<TestMediaSource?> { Value = staticSource };
        var animation = new KeyFrameAnimation<TestMediaSource?>();
        animation.KeyFrames.Add(keyframe);
        var property = (AnimatableProperty<TestMediaSource?>)holder.Source;
        property.Animation = animation;
        var (vm, control) = CreateControl(new AnimatablePropertyAdapter<TestMediaSource?>(property, holder));
        using var _ = vm;
        using var history = new HistoryScope(holder);
        vm.Accept(new Services(history.History));
        vm.EditingKeyFrame.Value = keyframe;
        string path = CreateFile("keyframe.psd");

        Confirm((StorageFileEditor)control, path);

        Assert.That(holder.Source.CurrentValue, Is.SameAs(staticSource));
        Assert.That(keyframe.Value!.Uri.LocalPath, Is.EqualTo(path));
        Assert.That(history.History.Undo(), Is.True);
        Assert.That(keyframe.Value, Is.SameAs(staticSource));
        Assert.That(history.History.Redo(), Is.True);
        Assert.That(keyframe.Value!.Uri.LocalPath, Is.EqualTo(path));
    }

    [AvaloniaTest]
    public void Failed_reads_keep_the_previous_source_and_allow_another_selection()
    {
        var holder = new FilePropertyHolder();
        var original = new TestMediaSource();
        string originalPath = CreateFile("original.psd");
        original.ReadFrom(new Uri(originalPath));
        holder.Source.CurrentValue = original;
        var (vm, control) = CreateControl(new EnginePropertyAdapter<TestMediaSource?>(holder.Source, holder));
        using var _ = vm;
        using var history = new HistoryScope(holder);
        vm.Accept(new Services(history.History));
        var editor = (StorageFileEditor)control;
        INotificationServiceHandler previous = NotificationService.Handler;
        var notifications = new RecordingNotifications();
        NotificationService.Handler = notifications;
        try
        {
            Assert.DoesNotThrow(() => Confirm(editor, CreateFile("invalid.psd", "invalid")));
            Assert.That(holder.Source.CurrentValue, Is.SameAs(original));
            Assert.That(editor.Value!.FullName, Is.EqualTo(originalPath));
            Assert.That(history.History.UndoCount, Is.Zero);
            Assert.That(notifications.Items, Has.Count.EqualTo(1));

            string valid = CreateFile("valid.psd");
            Confirm(editor, valid);
            Assert.That(holder.Source.CurrentValue!.Uri.LocalPath, Is.EqualTo(valid));
            Assert.That(history.History.UndoCount, Is.EqualTo(1));
        }
        finally { NotificationService.Handler = previous; }
    }

    [AvaloniaTest]
    public void Uninitialized_sources_display_an_empty_path_and_abstract_sources_keep_the_object_editor()
    {
        var holder = new FilePropertyHolder();
        holder.Source.CurrentValue = new TestMediaSource();
        var (vm, control) = CreateControl(new EnginePropertyAdapter<TestMediaSource?>(holder.Source, holder));
        using var _ = vm;
        Assert.That(((StorageFileEditor)control).Value, Is.Null);

        var adapter = new EnginePropertyAdapter<MediaSource?>(holder.AbstractSource, holder);
        Assert.That(PropertyEditorExtension.Instance.TryCreateContext([adapter], out var context), Is.True);
        using var __ = context;
        Assert.That(context, Is.TypeOf<CoreObjectEditorViewModel<MediaSource>>());
    }

    [AvaloniaTest]
    public void Locked_and_disposed_editors_do_not_change_the_source()
    {
        var holder = new FilePropertyHolder();
        var element = new Element { IsLocked = true };
        element.AddObject(holder);
        var (vm, control) = CreateControl(new EnginePropertyAdapter<TestMediaSource?>(holder.Source, holder));
        using var _ = vm;
        using var history = new HistoryScope(element);
        vm.Accept(new Services(history.History, element));
        var editor = (StorageFileEditor)control;
        string path = CreateFile("locked.psd");

        Confirm(editor, path);
        Assert.That(holder.Source.CurrentValue, Is.Null);
        Assert.That(editor.Value, Is.Null);
        Assert.That(history.History.UndoCount, Is.Zero);
        element.IsLocked = false;
        vm.Dispose();
        Assert.DoesNotThrow(() => Confirm(editor, path));
        Assert.That(holder.Source.CurrentValue, Is.Null);
    }

    [AvaloniaTest]
    public void Selected_custom_source_serializes_a_relative_path_and_round_trips()
    {
        var holder = new FilePropertyHolder();
        var (vm, control) = CreateControl(new EnginePropertyAdapter<TestMediaSource?>(holder.Source, holder));
        using var _ = vm;
        using var history = new HistoryScope(holder);
        vm.Accept(new Services(history.History));
        string path = CreateFile("asset.psd");
        Confirm((StorageFileEditor)control, path);
        var uri = new Uri(Path.Combine(_directory, "project.belm"));

        CoreSerializer.StoreToUri(holder, uri);

        Assert.That(JsonNode.Parse(File.ReadAllText(uri.LocalPath))![nameof(FilePropertyHolder.Source)]!
            .GetValue<string>(), Is.EqualTo("asset.psd"));
        var restored = CoreSerializer.RestoreFromUri<FilePropertyHolder>(uri);
        Assert.That(restored.Source.CurrentValue!.Uri.LocalPath, Is.EqualTo(path));
    }

    private string CreateFile(string name, string contents = "valid")
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, contents);
        return path;
    }

    private static void Confirm(StorageFileEditor editor, string? path)
    {
        FileInfo? oldValue = editor.Value;
        editor.Value = path == null ? null : new FileInfo(path);
        editor.RaiseEvent(new PropertyEditorValueChangedEventArgs<FileInfo?>(editor.Value, oldValue, PropertyEditor.ValueConfirmedEvent));
    }

    private static (BaseEditorViewModel Vm, Control Control) CreateControl(IPropertyAdapter adapter, EditorSurface surface = EditorSurface.Property)
    {
        PropertyEditorExtension extension = PropertyEditorExtension.Instance;
        Assert.That(extension.MatchProperty([adapter]), Is.EqualTo(new[] { adapter }));
        IPropertyEditorContext? context;
        Control? control;
        bool createdContext = surface switch
        {
            EditorSurface.Node => extension.TryCreateContextForNode([adapter], out context),
            EditorSurface.Settings => extension.TryCreateContextForSettings([adapter], out context),
            EditorSurface.ListItem => extension.TryCreateContextForListItem(adapter, out context),
            _ => extension.TryCreateContext([adapter], out context)
        };
        Assert.That(createdContext, Is.True);
        bool createdControl;
        if (surface == EditorSurface.ListItem)
        {
            createdControl = extension.TryCreateControlForListItem(context!, out var listControl);
            control = listControl as Control;
        }
        else
        {
            createdControl = surface switch
            {
                EditorSurface.Node => extension.TryCreateControlForNode(context!, out control),
                EditorSurface.Settings => extension.TryCreateControlForSettings(context!, out control),
                _ => extension.TryCreateControl(context!, out control)
            };
        }
        Assert.That(createdControl, Is.True);
        control!.DataContext = context;
        return ((BaseEditorViewModel)context!, control);
    }

    public enum EditorSurface { Property, Node, Settings, ListItem }

    [SuppressResourceClassGeneration]
    public sealed class FilePropertyHolder : EngineObject
    {
        public FilePropertyHolder() => ScanProperties<FilePropertyHolder>();

        [FileFilter("Photoshop documents", "*.psd", "*.psb")]
        public IProperty<TestMediaSource?> Source { get; } = Property.CreateAnimatable<TestMediaSource?>();

        public IProperty<TestFileSource?> PlainSource { get; } = Property.Create<TestFileSource?>();

        [FileFilter("Subtitles", "*.srt", MimeTypes = new[] { "application/x-subrip" })]
        [FileFilter("Text files", "*.txt", AppleUniformTypeIdentifiers = new[] { "public.plain-text" })]
        public IProperty<FileInfo?> File { get; } = Property.Create<FileInfo?>();

        [FileFilter("PNG images", "*.png")]
        public IProperty<ImageSource?> Image { get; } = Property.Create<ImageSource?>();

        public IProperty<CubeSource?> Cube { get; } = Property.Create<CubeSource?>();

        public IProperty<MediaSource?> AbstractSource { get; } = Property.Create<MediaSource?>();
    }

    [SuppressResourceClassGeneration]
    [JsonConverter(typeof(MediaSourceJsonConverter))]
    public sealed class TestMediaSource : MediaSource
    {
        public override void ReadFrom(Uri uri)
        {
            if (System.IO.File.ReadAllText(uri.LocalPath) == "invalid")
                throw new InvalidDataException("Invalid source file.");
            Uri = uri;
        }
    }

    [JsonConverter(typeof(FileSourceJsonConverter))]
    public sealed class TestFileSource : IFileSource
    {
        public Uri Uri { get; private set; } = null!;

        public void ReadFrom(Uri uri) => Uri = uri;
    }

    private sealed class HistoryScope : IDisposable
    {
        private readonly CoreObjectOperationObserver _observer;
        private readonly IDisposable _subscription;

        public HistoryScope(CoreObject target)
        {
            var sequence = new OperationSequenceGenerator();
            History = new HistoryManager(target, sequence);
            _observer = new CoreObjectOperationObserver(null, target, sequence);
            _subscription = History.Subscribe(_observer);
        }

        public HistoryManager History { get; }

        public void Dispose()
        {
            _subscription.Dispose();
            _observer.Dispose();
            History.Dispose();
        }
    }

    private sealed record Services(HistoryManager History, Element? Element = null) : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type type) => type == typeof(HistoryManager) ? History : type == typeof(Element) ? Element : null;

        public void Visit(IPropertyEditorContext context) { }
    }

    private sealed class RecordingNotifications : INotificationServiceHandler
    {
        public List<Notification> Items { get; } = [];

        public void Show(Notification notification) => Items.Add(notification);
    }
}
