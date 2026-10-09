using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Testing.Headless;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class StorageFileEditorTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"beutl-file-editor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (string path in Directory.GetFiles(_directory)) File.Delete(path);
        Directory.Delete(_directory);
    }

    [AvaloniaTest]
    [TestCase(PropertyEditorStyle.Normal)]
    [TestCase(PropertyEditorStyle.Compact)]
    [TestCase(PropertyEditorStyle.ListItem)]
    [TestCase(PropertyEditorStyle.Settings)]
    public void Drop_updates_the_value_and_confirms_once_even_with_a_focused_invalid_input(PropertyEditorStyle style)
    {
        string oldPath = CreateFile("previous.txt");
        string path = CreateFile("dropped.txt");
        var editor = new StorageFileEditor { Header = "File", EditorStyle = style, Value = new FileInfo(oldPath) };
        var next = new Button { Content = "Next" };
        var panel = new StackPanel { Children = { editor, next } };
        var window = new Window { Content = panel, Width = 760, Height = 200 };
        var confirmations = new List<PropertyEditorValueChangedEventArgs<FileInfo?>>();
        editor.ValueConfirmed += (_, e) => confirmations.Add((PropertyEditorValueChangedEventArgs<FileInfo?>)e);
        int parentDrops = 0;
        panel.AddHandler(DragDrop.DropEvent, (_, _) => parentDrops++);
        using var data = Transfer(StorageFile(path));
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            TextBox input = GetInput(editor);
            Assert.That(DragDrop.GetAllowDrop(input), Is.True);
            input.Focus();
            input.SelectAll();
            window.KeyTextInput(Path.Combine(_directory, "missing.txt"));
            Assert.That(DataValidationErrors.GetHasErrors(input), Is.True);

            Point point = GetDropPoint(window, editor);
            window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Copy);
            window.DragDrop(point, RawDragEventType.DragOver, data, DragDropEffects.Copy);
            Assert.That(input.Classes, Does.Contain("dragover"));
            window.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Copy);
            HeadlessTestHelpers.Render();
            next.Focus();
            HeadlessTestHelpers.Render();

            Assert.That(editor.Value?.FullName, Is.EqualTo(path));
            Assert.That(editor.Text, Is.EqualTo(path));
            Assert.That(input.Text, Is.EqualTo(path));
            Assert.That(DataValidationErrors.GetHasErrors(input), Is.False);
            Assert.That(input.Classes, Does.Not.Contain("dragover"));
            Assert.That(confirmations, Has.Count.EqualTo(1));
            Assert.That(confirmations[0].OldValue?.FullName, Is.EqualTo(oldPath));
            Assert.That(confirmations[0].NewValue?.FullName, Is.EqualTo(path));
            Assert.That(parentDrops, Is.Zero);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase("folder")]
    [TestCase("missing")]
    [TestCase("virtual")]
    [TestCase("unsupported")]
    [TestCase("read-only")]
    [TestCase("disabled")]
    [TestCase("disabled-parent")]
    [TestCase("text")]
    public void Invalid_drops_do_not_change_or_confirm_the_value(string kind)
    {
        string oldPath = CreateFile("previous.png");
        string path = CreateFile("dropped.png");
        var editor = new StorageFileEditor { Header = "File", Value = new FileInfo(oldPath) };
        var parent = new StackPanel { Children = { editor } };
        var window = new Window { Content = parent, Width = 760, Height = 200 };
        int confirmations = 0;
        editor.ValueConfirmed += (_, _) => confirmations++;
        using var data = new DataTransfer();
        switch (kind)
        {
            case "folder":
                var folder = new Mock<IStorageFolder>();
                folder.SetupGet(x => x.Path).Returns(new Uri(_directory));
                data.Add(DataTransferItem.CreateFile(folder.Object));
                break;
            case "missing":
                data.Add(DataTransferItem.CreateFile(StorageFile(Path.Combine(_directory, "missing.png"))));
                break;
            case "virtual":
                var file = new Mock<IStorageFile>();
                file.SetupGet(x => x.Path).Returns(new Uri("https://example.invalid/virtual.png"));
                data.Add(DataTransferItem.CreateFile(file.Object));
                break;
            case "text":
                data.Add(DataTransferItem.CreateText(path));
                break;
            default:
                data.Add(DataTransferItem.CreateFile(StorageFile(path)));
                break;
        }

        if (kind == "unsupported") editor.OpenOptions = Options("*.txt");
        if (kind == "read-only") editor.IsReadOnly = true;
        if (kind == "disabled") editor.IsEnabled = false;
        if (kind == "disabled-parent") parent.IsEnabled = false;
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            TextBox input = GetInput(editor);
            var over = RaiseDrag(input, DragDrop.DragOverEvent, data);
            var drop = RaiseDrag(input, DragDrop.DropEvent, data);

            Assert.That(over.DragEffects, Is.EqualTo(DragDropEffects.None));
            Assert.That(input.Classes, Does.Not.Contain("dragover"));
            Assert.That(editor.Value?.FullName, Is.EqualTo(oldPath));
            Assert.That(editor.Text, Is.EqualTo(oldPath));
            Assert.That(confirmations, Is.Zero);
            if (kind != "text")
            {
                Assert.That(over.Handled, Is.True);
                Assert.That(drop.Handled, Is.True);
                Assert.That(drop.DragEffects, Is.EqualTo(DragDropEffects.None));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase("empty")]
    [TestCase("case-insensitive")]
    [TestCase("all-files")]
    [TestCase("multiple-types")]
    public void Drop_uses_the_current_picker_patterns_and_the_first_matching_file(string filter)
    {
        string path = CreateFile(filter == "all-files" ? "LICENSE" : "dropped.PNG");
        var editor = new StorageFileEditor
        {
            Header = "File",
            OpenOptions = filter switch
            {
                "empty" => new FilePickerOpenOptions { FileTypeFilter = [] },
                "case-insensitive" => Options("*.png"),
                "all-files" => Options("*.*"),
                _ => new FilePickerOpenOptions
                {
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Audio") { Patterns = ["*.wav"] },
                        new FilePickerFileType("Images") { Patterns = ["*.jpg", "*.png"] }
                    ]
                }
            }
        };
        var window = new Window { Content = editor, Width = 760, Height = 100 };
        using var data = Transfer(StorageFile(Path.Combine(_directory, "missing.png")), StorageFile(path), StorageFile(CreateFile("second.PNG")));
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            TextBox input = GetInput(editor);
            var over = RaiseDrag(input, DragDrop.DragOverEvent, data);
            Assert.That(over.DragEffects, Is.EqualTo(DragDropEffects.Copy));
            RaiseDrag(input, DragDrop.DropEvent, data);
            Assert.That(editor.Value?.FullName, Is.EqualTo(path));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase("mime:image/png", "sample.png", true)]
    [TestCase("mime:image/png", "sample.PNG", true)]
    [TestCase("mime:image/png", "sample.jpg", false)]
    [TestCase("mime:image/*", "sample.webp", true)]
    [TestCase("mime:image/*", "sample.mp4", false)]
    [TestCase("mime:IMAGE/*", "sample.png", true)]
    [TestCase("mime:audio/x-wav", "sample.wav", true)]
    [TestCase("mime:video/mp4", "sample.m4v", true)]
    [TestCase("mime:text/plain", "sample.csv", true)]
    [TestCase("mime:text/plain", "sample.png", false)]
    [TestCase("mime:application/octet-stream", "LICENSE", true)]
    [TestCase("mime:image/png", "sample.unknown", false)]
    [TestCase("mime:application/x-custom", "sample.custom", false)]
    [TestCase("uti:public.png", "sample.png", true)]
    [TestCase("uti:public.png", "sample.jpg", false)]
    [TestCase("uti:public.image", "sample.JPEG", true)]
    [TestCase("uti:public.movie", "sample.mov", true)]
    [TestCase("uti:public.movie", "sample.png", false)]
    [TestCase("uti:public.audiovisual-content", "sample.mp3", true)]
    [TestCase("uti:public.data", "LICENSE", true)]
    [TestCase("uti:public.image", "sample.unknown", false)]
    [TestCase("uti:com.example.custom", "sample.png", false)]
    [TestCase("mime:image/png uti:public.jpeg", "sample.jpg", true)]
    [TestCase("mime:image/png uti:public.jpeg", "sample.gif", false)]
    [TestCase("pattern:*.png mime:image/* uti:public.image", "sample.png", true)]
    [TestCase("pattern:*.png mime:image/* uti:public.image", "sample.jpg", false)]
    public void Drop_matches_mime_types_and_utis_only_when_the_filter_has_no_patterns(string filter, string name, bool accepted)
    {
        string path = CreateFile(name);
        var type = new FilePickerFileType("Files");
        foreach (var group in filter.Split(' ').Select(x => x.Split(':', 2)).GroupBy(x => x[0], x => x[1]))
        {
            switch (group.Key)
            {
                case "pattern": type.Patterns = group.ToArray(); break;
                case "mime": type.MimeTypes = group.ToArray(); break;
                case "uti": type.AppleUniformTypeIdentifiers = group.ToArray(); break;
            }
        }
        var editor = new StorageFileEditor { Header = "File", OpenOptions = new() { FileTypeFilter = [type] } };
        var window = new Window { Content = editor, Width = 760, Height = 100 };
        using var data = Transfer(StorageFile(path));
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            TextBox input = GetInput(editor);
            var over = RaiseDrag(input, DragDrop.DragOverEvent, data);
            Assert.That(over.DragEffects, Is.EqualTo(accepted ? DragDropEffects.Copy : DragDropEffects.None));
            RaiseDrag(input, DragDrop.DropEvent, data);
            Assert.That(editor.Value?.FullName, Is.EqualTo(accepted ? path : null));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Drop_revalidates_changes_since_drag_enter(bool readOnly)
    {
        var editor = new StorageFileEditor { Header = "File", OpenOptions = Options("*.png") };
        var window = new Window { Content = editor, Width = 760, Height = 100 };
        using var data = Transfer(StorageFile(CreateFile("dropped.png")));
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            TextBox input = GetInput(editor);
            Assert.That(RaiseDrag(input, DragDrop.DragEnterEvent, data).DragEffects, Is.EqualTo(DragDropEffects.Copy));
            if (readOnly) editor.IsReadOnly = true;
            else editor.OpenOptions.FileTypeFilter = Options("*.txt").FileTypeFilter;
            Assert.That(RaiseDrag(input, DragDrop.DragOverEvent, data).DragEffects, Is.EqualTo(DragDropEffects.None));
            RaiseDrag(input, DragDrop.DropEvent, data);
            Assert.That(editor.Value, Is.Null);
            Assert.That(input.Classes, Does.Not.Contain("dragover"));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void Drag_leave_clears_the_highlight_without_selecting_the_file()
    {
        var editor = new StorageFileEditor { Header = "File" };
        var window = new Window { Content = editor, Width = 760, Height = 100 };
        using var data = Transfer(StorageFile(CreateFile("dropped.txt")));
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Point point = GetDropPoint(window, editor);
            window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Copy);
            Assert.That(GetInput(editor).Classes, Does.Contain("dragover"));
            window.DragDrop(point, RawDragEventType.DragLeave, data, DragDropEffects.Copy);
            Assert.That(GetInput(editor).Classes, Does.Not.Contain("dragover"));
            Assert.That(editor.Value, Is.Null);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void Changing_templates_does_not_duplicate_drop_confirmations()
    {
        var editor = new StorageFileEditor { Header = "File" };
        var window = new Window { Content = editor, Width = 760, Height = 100 };
        int confirmations = 0;
        editor.ValueConfirmed += (_, _) => confirmations++;
        using var data = Transfer(StorageFile(CreateFile("dropped.txt")));
        try
        {
            window.Show();
            foreach (var style in new[] { PropertyEditorStyle.Normal, PropertyEditorStyle.Compact, PropertyEditorStyle.ListItem, PropertyEditorStyle.Settings, PropertyEditorStyle.Normal })
            {
                editor.EditorStyle = style;
                HeadlessTestHelpers.Render(3);
                Point point = GetDropPoint(window, editor);
                window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Copy);
                window.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Copy);
            }
            Assert.That(confirmations, Is.EqualTo(5));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void Picking_a_file_also_confirms_once_when_the_input_loses_focus()
    {
        string path = CreateFile("selected.txt");
        var editor = new StorageFileEditor { Header = "File" };
        var next = new Button { Content = "Next" };
        var window = new Window { Content = new StackPanel { Children = { editor, next } }, Width = 760, Height = 200 };
        var storage = new Mock<IStorageProvider>();
        storage.Setup(x => x.OpenFilePickerAsync(editor.OpenOptions)).ReturnsAsync(new[] { StorageFile(path) });
        TestStorageProviderFactory.SetProvider(window, storage.Object);
        int confirmations = 0;
        editor.ValueConfirmed += (_, _) => confirmations++;
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            GetInput(editor).Focus();
            editor.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "PART_Button")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            next.Focus();
            HeadlessTestHelpers.Render();
            Assert.That(editor.Value?.FullName, Is.EqualTo(path));
            Assert.That(confirmations, Is.EqualTo(1));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [Explicit("Produces headless PNG captures for visual review.")]
    [TestCase(false)]
    [TestCase(true)]
    public void Capture_drop_states(bool light)
    {
        string output = Environment.GetEnvironmentVariable("BEUTL_FILE_EDITOR_CAPTURE_DIR")
            ?? Path.Combine(Path.GetTempPath(), "beutl-file-editor-captures");
        Directory.CreateDirectory(output);
        ThemeVariant? previousTheme = Application.Current!.RequestedThemeVariant;
        Application.Current.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(16) };
        var editors = new[] { PropertyEditorStyle.Normal, PropertyEditorStyle.Compact, PropertyEditorStyle.ListItem, PropertyEditorStyle.Settings }
            .Select(style => new StorageFileEditor { Header = $"{style} file", EditorStyle = style, OpenOptions = Options("*.png") }).ToArray();
        foreach (var editor in editors) panel.Children.Add(editor);
        var window = new Window { Content = panel, Width = 760, Height = 310 };
        using var data = Transfer(StorageFile(CreateFile("sample.png")));
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            Capture("before");
            foreach (var editor in editors) RaiseDrag(GetInput(editor), DragDrop.DragEnterEvent, data);
            HeadlessTestHelpers.Render(3);
            Capture("hover");
            foreach (var editor in editors)
            {
                Assert.That(GetInput(editor).BorderBrush,
                    Is.SameAs(editor.FindResource(editor.ActualThemeVariant, "TextControlBorderBrushFocused")), editor.EditorStyle.ToString());
            }
            foreach (var editor in editors) RaiseDrag(GetInput(editor), DragDrop.DropEvent, data);
            HeadlessTestHelpers.Render(3);
            Capture("after");
        }
        finally
        {
            window.Close();
            Application.Current.RequestedThemeVariant = previousTheme;
        }

        void Capture(string state)
        {
            using var image = window.CaptureRenderedFrame();
            Assert.That(image, Is.Not.Null);
            image!.Save(Path.Combine(output, $"{(light ? "light" : "dark")}-{state}.png"), PngBitmapEncoderOptions.Default);
        }
    }

    private string CreateFile(string name)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, "sample");
        return path;
    }

    private static IStorageFile StorageFile(string path)
    {
        var file = new Mock<IStorageFile>();
        file.SetupGet(x => x.Path).Returns(new Uri(path));
        file.SetupGet(x => x.Name).Returns(Path.GetFileName(path));
        return file.Object;
    }

    private static FilePickerOpenOptions Options(params string[] patterns)
        => new() { FileTypeFilter = [new FilePickerFileType("Files") { Patterns = patterns }] };

    private static DataTransfer Transfer(params IStorageItem[] items)
    {
        var data = new DataTransfer();
        foreach (var item in items) data.Add(DataTransferItem.CreateFile(item));
        return data;
    }

    private static TextBox GetInput(StorageFileEditor editor)
        => editor.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "PART_InnerTextBox");

    private static Point GetDropPoint(Window window, StorageFileEditor editor)
    {
        TextBox input = GetInput(editor);
        return input.TranslatePoint(new Point(input.Bounds.Width / 2, input.Bounds.Height / 2), window)!.Value;
    }

    private static DragEventArgs RaiseDrag(TextBox input, RoutedEvent<DragEventArgs> routedEvent, IDataTransfer data)
    {
        var args = new DragEventArgs(routedEvent, data, input, new Point(10, 10), KeyModifiers.None)
        {
            DragEffects = DragDropEffects.Copy
        };
        input.RaiseEvent(args);
        return args;
    }
}
