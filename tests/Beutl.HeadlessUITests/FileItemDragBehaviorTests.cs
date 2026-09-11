using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Xaml.Interactivity;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.Components.FileBrowserTab.Views;
using Beutl.Testing.Headless;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class FileItemDragBehaviorTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Drag_resolves_storage_from_the_window_and_releases_its_state(bool isDirectory)
    {
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, "drag-source.bin");
        var folderUri = new Uri(path + Path.DirectorySeparatorChar);
        using var item = new FileSystemItemViewModel(path, isDirectory);
        var control = new Border { Background = Avalonia.Media.Brushes.White, DataContext = item };
        var behavior = new FileItemDragBehavior();
        Interaction.GetBehaviors(control).Add(behavior);
        var file = new Mock<IStorageFile>();
        var folder = new Mock<IStorageFolder>();
        var fileSource = new TaskCompletionSource<IStorageFile?>();
        var folderSource = new TaskCompletionSource<IStorageFolder?>();
        var storage = new Mock<IStorageProvider>(MockBehavior.Strict);
        storage.Setup(provider => provider.TryGetFileFromPathAsync(new Uri(path))).Returns(fileSource.Task);
        storage.Setup(provider => provider.TryGetFolderFromPathAsync(folderUri)).Returns(folderSource.Task);
        var window = new Window { Content = control, Width = 300, Height = 200 };
        TestStorageProviderFactory.SetProvider(window, storage.Object);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            window.MouseDown(new Point(20, 20), MouseButton.Left);
            window.MouseMove(new Point(22, 22), RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Settle();
            Assert.That(FileItemDragBehavior.IsInternalDragInProgress, Is.False,
                "Moving below the threshold must not start a drag.");
            window.MouseMove(new Point(40, 40), RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Settle();
            window.MouseMove(new Point(60, 60), RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Settle();
            Assert.That(FileItemDragBehavior.IsInternalDragInProgress, Is.True);
            storage.Verify(provider => provider.TryGetFileFromPathAsync(new Uri(path)),
                isDirectory ? Times.Never() : Times.Once());
            storage.Verify(provider => provider.TryGetFolderFromPathAsync(folderUri),
                isDirectory ? Times.Once() : Times.Never());

            fileSource.SetResult(file.Object);
            folderSource.SetResult(folder.Object);
            HeadlessTestHelpers.Settle();
            window.MouseUp(new Point(60, 60), MouseButton.Left);
            for (int attempt = 0; attempt < 100 && FileItemDragBehavior.IsInternalDragInProgress; attempt++)
            {
                await Task.Delay(10);
                HeadlessTestHelpers.Settle();
            }

            Assert.That(FileItemDragBehavior.IsInternalDragInProgress, Is.False,
                "Completing or cancelling the drag must clear the shared internal-drag flag.");
            window.MouseMove(new Point(80, 80));
            Assert.That(FileItemDragBehavior.IsInternalDragInProgress, Is.False);
        }
        finally
        {
            fileSource.TrySetResult(null);
            folderSource.TrySetResult(null);
            window.MouseUp(new Point(60, 60), MouseButton.Left);
            Interaction.GetBehaviors(control).Remove(behavior);
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
