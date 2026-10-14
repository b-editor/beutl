using System.Reflection;
using Avalonia;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Beutl.Controls;
using Beutl.Testing.Headless;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class FileInputAreaTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Dropping_a_local_file_without_a_filter_selects_it(bool setOptions)
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-drop-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "sample");
        var input = new FileInputArea
        {
            OpenOptions = setOptions ? new FilePickerOpenOptions { FileTypeFilter = [] } : null,
        };
        var host = new ControlHost(input, 400, 200);
        try
        {
            HeadlessTestHelpers.Render();
            IStorageFile? file = await host.Window.StorageProvider.TryGetFileFromPathAsync(new Uri(path));
            Assert.That(file, Is.Not.Null);

            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateFile(file!));
            var enter = new DragEventArgs(DragDrop.DragEnterEvent, data, input, new Point(10, 10), KeyModifiers.None);
            input.RaiseEvent(enter);
            Assert.That(enter.DragEffects, Is.EqualTo(DragDropEffects.Copy));

            var drop = new DragEventArgs(DragDrop.DropEvent, data, input, new Point(10, 10), KeyModifiers.None)
            {
                DragEffects = enter.DragEffects,
            };
            input.RaiseEvent(drop);
            Assert.That(input.SelectedFile, Is.SameAs(file));
        }
        finally
        {
            host.Dispose();
            File.Delete(path);
        }
    }

    [AvaloniaTest]
    public async Task Dropping_a_file_outside_an_explicit_filter_is_rejected()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-drop-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "sample");
        var input = new FileInputArea
        {
            OpenOptions = new FilePickerOpenOptions
            {
                FileTypeFilter = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }],
            },
        };
        var host = new ControlHost(input, 400, 200);
        try
        {
            HeadlessTestHelpers.Render();
            IStorageFile? file = await host.Window.StorageProvider.TryGetFileFromPathAsync(new Uri(path));
            Assert.That(file, Is.Not.Null);

            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateFile(file!));
            var enter = new DragEventArgs(DragDrop.DragEnterEvent, data, input, new Point(10, 10), KeyModifiers.None);
            input.RaiseEvent(enter);

            Assert.That(enter.DragEffects, Is.EqualTo(DragDropEffects.None));
            Assert.That(input.SelectedFile, Is.Null);
        }
        finally
        {
            host.Dispose();
            File.Delete(path);
        }
    }

    [AvaloniaTest]
    public void Dropping_a_virtual_file_without_a_filter_selects_it()
    {
        IStorageFile file = DispatchProxy.Create<IStorageFile, VirtualFileProxy>();
        Assert.That(file.TryGetLocalPath(), Is.Null);

        var input = new FileInputArea();
        using var host = new ControlHost(input, 400, 200);
        HeadlessTestHelpers.Render();

        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(file));
        var enter = new DragEventArgs(DragDrop.DragEnterEvent, data, input, new Point(10, 10), KeyModifiers.None);
        input.RaiseEvent(enter);
        Assert.That(enter.DragEffects, Is.EqualTo(DragDropEffects.Copy));

        var drop = new DragEventArgs(DragDrop.DropEvent, data, input, new Point(10, 10), KeyModifiers.None)
        {
            DragEffects = enter.DragEffects,
        };
        input.RaiseEvent(drop);
        Assert.That(input.SelectedFile, Is.SameAs(file));
    }

    public class VirtualFileProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name switch
            {
                "get_Path" => new Uri("https://example.com/virtual.txt"),
                "get_Name" => "virtual.txt",
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
    }
}
