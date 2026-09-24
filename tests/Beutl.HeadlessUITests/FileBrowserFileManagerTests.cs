using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.Components.FileBrowserTab.Views;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Services;
using Beutl.Testing.Headless;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class FileBrowserFileManagerTests
{
    [Test]
    public async Task Started_helper_reports_a_nonzero_exit()
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.dll"));

        Assert.That(await FileManagerLauncher.LaunchAsync(startInfo), Is.False);
    }

    [Test]
    public void File_launch_reveals_the_file_where_supported()
    {
        const string unixPath = "/tmp/a file.txt";
        const string windowsPath = @"C:\some folder\a file.txt";

        var mac = FileManagerLauncher.CreateStartInfo(unixPath, false, OSPlatform.OSX);
        var windows = FileManagerLauncher.CreateStartInfo(windowsPath, false, OSPlatform.Windows);
        var linux = FileManagerLauncher.CreateStartInfo(unixPath, false, OSPlatform.Linux);

        Assert.Multiple(() =>
        {
            Assert.That(mac.FileName, Is.EqualTo("/usr/bin/open"));
            Assert.That(mac.ArgumentList, Is.EqualTo(new[] { "-R", unixPath }));
            Assert.That(mac.UseShellExecute, Is.False);
            Assert.That(windows.FileName, Does.EndWith("explorer.exe"));
            Assert.That(windows.Arguments, Is.EqualTo($"/select,\"{windowsPath}\""));
            Assert.That(windows.UseShellExecute, Is.False);
            Assert.That(linux.FileName, Is.EqualTo("xdg-open"));
            Assert.That(linux.ArgumentList, Is.EqualTo(new[] { "/tmp" }));
            Assert.That(linux.UseShellExecute, Is.False);
        });
    }

    [Test]
    public void Directory_launch_opens_the_directory()
    {
        const string unixPath = "/tmp/a folder";
        const string windowsPath = @"C:\some folder";

        var mac = FileManagerLauncher.CreateStartInfo(unixPath, true, OSPlatform.OSX);
        var windows = FileManagerLauncher.CreateStartInfo(windowsPath, true, OSPlatform.Windows);
        var linux = FileManagerLauncher.CreateStartInfo(unixPath, true, OSPlatform.Linux);

        Assert.Multiple(() =>
        {
            Assert.That(mac.ArgumentList, Is.EqualTo(new[] { unixPath }));
            Assert.That(windows.ArgumentList, Is.EqualTo(new[] { windowsPath }));
            Assert.That(linux.ArgumentList, Is.EqualTo(new[] { unixPath }));
        });

        Assert.That(FileManagerLauncher.CreateStartInfo(@"C:\", true, OSPlatform.Windows).ArgumentList,
            Is.EqualTo(new[] { @"C:\" }));
    }

    [AvaloniaTest]
    public void Right_click_menu_shows_the_platform_action_for_files_and_directories()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-file-manager-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "sample.txt");
        File.WriteAllText(file, "sample");
        string directory = Path.Combine(root, "folder");
        Directory.CreateDirectory(directory);

        var vm = new FileBrowserTabViewModel(new Mock<IEditorContext>().Object);
        var view = new FileBrowserTabView { DataContext = vm };
        var window = new Window { Content = view, Width = 400, Height = 360 };
        using var notifications = new NotificationCapture();
        var launches = new List<ProcessStartInfo>();
        vm.LaunchFileManagerAsync = startInfo =>
        {
            launches.Add(startInfo);
            return Task.FromResult(false);
        };
        try
        {
            vm.ViewMode.Value = FileBrowserViewMode.List;
            vm.RootPath.Value = root;
            window.Show();
            HeadlessTestHelpers.Render();

            foreach (string path in new[] { file, directory })
            {
                FileSystemItemViewModel item = vm.Items.Single(x => x.FullPath == path);
                Border border = view.GetVisualDescendants().OfType<Border>()
                    .Single(x => x.Classes.Contains("file-item") && ReferenceEquals(x.DataContext, item));
                Point point = border.TranslatePoint(new Point(8, border.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                window.MouseDown(point, MouseButton.Right);
                window.MouseUp(point, MouseButton.Right);
                HeadlessTestHelpers.Render();

                ContextMenu menu = border.ContextMenu!;
                Assert.That(menu.IsOpen, Is.True);
                MenuItem action = menu.Items.OfType<MenuItem>()
                    .Single(x => Equals(x.Tag, "OpenInFileManager"));
                Assert.Multiple(() =>
                {
                    Assert.That(action.Header, Is.EqualTo(FileManagerLauncher.MenuHeader));
                    Assert.That(action.Bounds.Height, Is.GreaterThan(0));
                    Assert.That(action.GetVisualDescendants().OfType<TextBlock>()
                        .Any(x => x.Text == FileManagerLauncher.MenuHeader), Is.True);
                });

                if (Environment.GetEnvironmentVariable("BEUTL_FILE_MANAGER_MENU_CAPTURE") is { Length: > 0 } capture)
                {
                    Directory.CreateDirectory(capture);
                    using var image = TopLevel.GetTopLevel(menu)?.CaptureRenderedFrame();
                    image?.Save(Path.Combine(capture, item.IsDirectory ? "folder-menu.png" : "file-menu.png"),
                        PngBitmapEncoderOptions.Default);
                }

                action.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                HeadlessTestHelpers.Settle();
                ProcessStartInfo expected = FileManagerLauncher.CreateStartInfo(path, item.IsDirectory);
                Assert.Multiple(() =>
                {
                    Assert.That(launches, Has.Count.EqualTo(notifications.Notifications.Count));
                    Assert.That(launches[^1].FileName, Is.EqualTo(expected.FileName));
                    Assert.That(launches[^1].Arguments, Is.EqualTo(expected.Arguments));
                    Assert.That(launches[^1].ArgumentList, Is.EqualTo(expected.ArgumentList));
                    Assert.That(notifications.Notifications[^1].Type, Is.EqualTo(NotificationType.Error));
                    Assert.That(notifications.Notifications[^1].Message, Is.EqualTo(MessageStrings.OperationFailed));
                });

                menu.Close();
                HeadlessTestHelpers.Render();
            }
        }
        finally
        {
            window.Close();
            vm.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaTest]
    public async Task Missing_item_and_launch_exception_are_reported()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-file-manager-errors-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "sample.txt");
        File.WriteAllText(file, "sample");
        using var vm = new FileBrowserTabViewModel(new Mock<IEditorContext>().Object);
        using var item = new FileSystemItemViewModel(file, isDirectory: false);
        using var notifications = new NotificationCapture();
        int launches = 0;
        try
        {
            vm.LaunchFileManagerAsync = _ =>
            {
                launches++;
                throw new InvalidOperationException("Simulated launcher failure");
            };
            await vm.OpenInFileManagerAsync(item);
            File.Delete(file);
            await vm.OpenInFileManagerAsync(item);

            Assert.Multiple(() =>
            {
                Assert.That(launches, Is.EqualTo(1));
                Assert.That(notifications.Notifications.Select(x => x.Type),
                    Is.EqualTo(new[] { NotificationType.Error, NotificationType.Error }));
                Assert.That(notifications.Notifications.Select(x => x.Message),
                    Is.EqualTo(new[] { MessageStrings.OperationFailed, MessageStrings.FileDoesNotExist }));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaTest]
    public async Task Dangling_file_link_can_still_be_revealed()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("Creating symbolic links may require privileges on Windows.");

        string root = Path.Combine(Path.GetTempPath(), $"beutl-file-manager-link-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string link = Path.Combine(root, "lost.txt");
            File.CreateSymbolicLink(link, Path.Combine(root, "missing.txt"));
            using var vm = new FileBrowserTabViewModel(new Mock<IEditorContext>().Object);
            vm.RootPath.Value = root;
            FileSystemItemViewModel item = vm.Items.Single(x => x.FullPath == link);
            using var notifications = new NotificationCapture();
            int launches = 0;
            vm.LaunchFileManagerAsync = _ => { launches++; return Task.FromResult(true); };

            await vm.OpenInFileManagerAsync(item);

            Assert.That(launches, Is.EqualTo(1));
            Assert.That(notifications.Notifications, Is.Empty);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class NotificationCapture : INotificationServiceHandler, IDisposable
    {
        private readonly INotificationServiceHandler _previous = NotificationService.Handler;

        public NotificationCapture() => NotificationService.Handler = this;

        public List<Notification> Notifications { get; } = [];

        public void Show(Notification notification) => Notifications.Add(notification);

        public void Dispose() => NotificationService.Handler = _previous;
    }
}
