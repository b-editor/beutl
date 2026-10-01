using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Extensibility;
using Beutl.Graphics3D.Models;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Editors;
using Beutl.Views.Editors;

namespace Beutl.HeadlessUITests;

public class ModelSourceEditorTests
{
    [AvaloniaTest]
    public async Task Initial_source_selection_can_be_undone_and_redone()
    {
        string directory = Directory.CreateTempSubdirectory("model-editor-history-").FullName;
        try
        {
            string path = Path.Combine(directory, "triangle.obj");
            File.WriteAllText(path, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
            var model = new Model3D();
            var sequence = new OperationSequenceGenerator();
            using var history = new HistoryManager(model, sequence);
            using var observer = new CoreObjectOperationObserver(null, model, sequence);
            using var subscription = history.Subscribe(observer);
            using var vm = new ModelSourceEditorViewModel(new EnginePropertyAdapter<ModelSource?>(model.Source, model));
            vm.Accept(new Services(history));
            var view = new ModelSourceEditor { DataContext = vm };
            var editor = view.FindControl<StorageFileEditor>("FileEditor")!;
            editor.Value = new FileInfo(path);

            editor.RaiseEvent(new PropertyEditorValueChangedEventArgs<FileInfo?>(editor.Value, null, PropertyEditor.ValueConfirmedEvent));
            await view.LoadingTask;
            var child = model.Children.Single();
            Assert.That(history.UndoCount, Is.EqualTo(1));
            Assert.That(history.Undo(), Is.True);
            Assert.That(model.Source.CurrentValue, Is.Null);
            Assert.That(model.Children, Is.Empty);
            Assert.That(history.Redo(), Is.True);
            Assert.That(model.Children.Single(), Is.SameAs(child));
            Assert.That(model.Source.CurrentValue!.Uri.LocalPath, Is.EqualTo(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Invalid_model_keeps_the_previous_source_and_shows_a_recoverable_error(bool dark)
    {
        string directory = Directory.CreateTempSubdirectory("model-editor-").FullName;
        var window = new Window { Width = 340, Height = 300, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            string valid = Path.Combine(directory, "triangle.obj");
            string invalid = Path.Combine(directory, "truncated.glb");
            File.WriteAllText(valid, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
            File.WriteAllText(invalid, "glTF");
            var original = new ModelSource(); original.ReadFrom(new Uri(valid));
            var model = new Model3D(); model.Source.CurrentValue = original;
            var originalChild = model.Children.Single();
            using var history = new HistoryManager(model, new OperationSequenceGenerator());
            using var vm = new ModelSourceEditorViewModel(new EnginePropertyAdapter<ModelSource?>(model.Source, model));
            vm.Accept(new Services(history));
            var view = new ModelSourceEditor { DataContext = vm, Margin = new Thickness(16) };
            window.Content = view; window.Show(); HeadlessTestHelpers.Render();
            var editor = view.FindControl<StorageFileEditor>("FileEditor")!;
            var message = view.FindControl<TextBlock>("message")!;
            var progress = view.FindControl<ProgressBar>("progress")!;

            editor.Value = new FileInfo(invalid);
            Assert.DoesNotThrow(() => editor.RaiseEvent(new PropertyEditorValueChangedEventArgs<FileInfo?>(editor.Value, new FileInfo(valid), PropertyEditor.ValueConfirmedEvent)));
            Assert.That(progress.IsVisible, Is.True);
            await view.LoadingTask;
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(model.Source.CurrentValue, Is.SameAs(original));
                Assert.That(model.Children.Single(), Is.SameAs(originalChild));
                Assert.That(editor.Value!.FullName, Is.EqualTo(valid));
                Assert.That(message.IsEffectivelyVisible, Is.True);
                Assert.That(progress.IsVisible, Is.False);
                Assert.That(message.Text, Is.Not.Empty);
                Assert.That(message.Bounds.Right, Is.LessThanOrEqualTo(view.Bounds.Width + 1));
            });
            if (Environment.GetEnvironmentVariable("BEUTL_MODEL_EDITOR_CAPTURE") is { Length: > 0 } capture)
            {
                Directory.CreateDirectory(capture);
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(capture, dark ? "model-error-dark.png" : "model-error-light.png"), PngBitmapEncoderOptions.Default);
            }

            editor.Value = new FileInfo(valid);
            editor.RaiseEvent(new PropertyEditorValueChangedEventArgs<FileInfo?>(editor.Value, editor.Value, PropertyEditor.ValueConfirmedEvent));
            await view.LoadingTask;
            Assert.That(model.Source.CurrentValue!.MeshCount, Is.EqualTo(1));
            Assert.That(message.IsVisible, Is.False, message.Text);
        }
        finally { window.Close(); Directory.Delete(directory, recursive: true); }
    }

    [AvaloniaTest]
    public async Task Superseded_selection_is_discarded_even_if_it_finishes_later()
    {
        string directory = Directory.CreateTempSubdirectory("model-editor-race-").FullName;
        try
        {
            string valid = Path.Combine(directory, "triangle.obj");
            string invalid = Path.Combine(directory, "truncated.glb");
            File.WriteAllText(valid, "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
            File.WriteAllText(invalid, "glTF");
            var model = new Model3D();
            using var history = new HistoryManager(model, new OperationSequenceGenerator());
            using var vm = new ModelSourceEditorViewModel(new EnginePropertyAdapter<ModelSource?>(model.Source, model));
            vm.Accept(new Services(history));
            var view = new ModelSourceEditor { DataContext = vm };
            var editor = view.FindControl<StorageFileEditor>("FileEditor")!;
            var message = view.FindControl<TextBlock>("message")!;

            editor.Value = new FileInfo(invalid);
            editor.RaiseEvent(new PropertyEditorValueChangedEventArgs<FileInfo?>(editor.Value, null, PropertyEditor.ValueConfirmedEvent));
            Task first = view.LoadingTask;
            editor.Value = new FileInfo(valid);
            editor.RaiseEvent(new PropertyEditorValueChangedEventArgs<FileInfo?>(editor.Value, null, PropertyEditor.ValueConfirmedEvent));
            await Task.WhenAll(first, view.LoadingTask);

            Assert.Multiple(() =>
            {
                Assert.That(model.Source.CurrentValue!.Uri.LocalPath, Is.EqualTo(valid));
                Assert.That(editor.Value!.FullName, Is.EqualTo(valid));
                Assert.That(message.IsVisible, Is.False, message.Text);
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed record Services(HistoryManager History) : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type type) => type == typeof(HistoryManager) ? History : null;
        public void Visit(IPropertyEditorContext context) { }
    }
}
