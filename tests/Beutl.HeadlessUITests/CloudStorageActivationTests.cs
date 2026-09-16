using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Testing.Headless;
using Beutl.Views.Tools;
using static Beutl.HeadlessUITests.CloudStorageTests;
using StorageScope = Beutl.HeadlessUITests.CloudStorageIncrementalTests.StorageScope;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class CloudStorageActivationTests
{
    [AvaloniaTest]
    [TestCase(FileBrowserViewMode.Icon, false)]
    [TestCase(FileBrowserViewMode.List, false)]
    [TestCase(FileBrowserViewMode.Icon, true)]
    [TestCase(FileBrowserViewMode.List, true)]
    public async Task FileActivationUsesTheAuthenticatedOpenAction(FileBrowserViewMode mode, bool keyboard)
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        vm.ViewMode.Value = mode;
        await scope.LoadFirstAsync(Response());
        Uri? launched = null;
        var view = new CloudStorageView
        {
            DataContext = vm,
            UriLauncher = uri => { launched = uri; return Task.CompletedTask; },
        };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            ActivateFile(window, view, keyboard);
            await WaitFor(() => scope.Handler.Requests.Count == 2);
            var request = scope.Handler.Requests[1];
            Assert.That(request.Uri.AbsolutePath, Is.EqualTo("/api/v3/storage/files/file"));
            Assert.That(request.Authorization, Is.EqualTo("Bearer token-a"));
            var metadata = JsonNode.Parse(Response())!["entries"]![1]!.DeepClone();
            metadata["contentUrl"] = "https://beutl.com/api/v3/files/file/content";
            request.Complete(metadata.ToJsonString());
            await WaitFor(() => launched != null || vm.ActionError.Value != null);
            Assert.That(vm.ActionError.Value, Is.Null);
            Assert.That(launched!.AbsoluteUri, Is.EqualTo("https://beutl.com/api/v3/files/file/content"));
            Assert.That(vm.Items.Last().Id, Is.EqualTo("file"));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task FileActivationRequiresTheOpenCapability(bool keyboard)
    {
        await using var scope = new StorageScope();
        var response = JsonNode.Parse(Response())!;
        response["entries"]![1]!["actions"] = new JsonArray("download", "details");
        await scope.LoadFirstAsync(response.ToJsonString());
        bool launched = false;
        var view = new CloudStorageView
        {
            DataContext = scope.ViewModel,
            UriLauncher = _ => { launched = true; return Task.CompletedTask; },
        };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            ActivateFile(window, view, keyboard);
            HeadlessTestHelpers.Settle();
            Assert.That(scope.Handler.Requests, Has.Count.EqualTo(1));
            Assert.That(launched, Is.False);
        }
        finally { window.Close(); }
    }

    private static void ActivateFile(Window window, CloudStorageView view, bool keyboard)
    {
        var list = view.FindControl<ListBox>("StorageItems")!;
        list.SelectedIndex = 1;
        var container = list.ContainerFromIndex(1)!;
        if (keyboard)
        {
            container.Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        }
        else
        {
            var position = container.TranslatePoint(new Point(20, 20), window)!.Value;
            window.MouseMove(position);
            for (int click = 0; click < 2; click++)
            {
                window.MouseDown(position, MouseButton.Left);
                window.MouseUp(position, MouseButton.Left);
            }
        }
    }
}
