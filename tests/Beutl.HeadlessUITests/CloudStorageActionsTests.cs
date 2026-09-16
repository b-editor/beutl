using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Language;
using Beutl.Testing.Headless;
using Beutl.Views.Tools;
using static Beutl.HeadlessUITests.CloudStorageTests;
using StorageScope = Beutl.HeadlessUITests.CloudStorageIncrementalTests.StorageScope;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class CloudStorageActionsTests
{
    [AvaloniaTest]
    [TestCase(FileBrowserViewMode.Icon, "PRIVATE")]
    [TestCase(FileBrowserViewMode.List, "PRIVATE")]
    [TestCase(FileBrowserViewMode.Icon, "PUBLIC")]
    [TestCase(FileBrowserViewMode.List, "PUBLIC")]
    [TestCase(FileBrowserViewMode.Icon, "DEDICATED")]
    [TestCase(FileBrowserViewMode.List, "DEDICATED")]
    public async Task RightClickTargetsTheClickedFileAndMatchesWebActions(FileBrowserViewMode mode, string visibility)
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        vm.ViewMode.Value = mode;
        await scope.LoadFirstAsync(Response(visibility: visibility));
        var view = new CloudStorageView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        try
        {
            window.Show(); HeadlessTestHelpers.Render();
            var list = view.FindControl<ListBox>("StorageItems")!;
            list.SelectedIndex = 0;
            RightClick(window, list.ContainerFromIndex(1)!);
            Assert.That(view.StorageMenu?.IsOpen, Is.True);
            Assert.That(list.SelectedItem, Is.SameAs(vm.Items[1]));
            var expected = visibility switch
            {
                "PRIVATE" => new[] { "open", "download", "rename", "move", "details", "setPublic", "delete" },
                "PUBLIC" => ["open", "download", "copyLink", "rename", "move", "details", "setPrivate", "delete"],
                _ => ["open", "download", "move", "details"],
            };
            Assert.That(ActionIds(view), Is.EqualTo(expected));
            Capture(view.StorageMenu!, $"menu-{mode}-{visibility}");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task FolderAndEmptySpaceHaveTheirOwnMenusAndKeyboardContextMenuWorks()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response());
        var vm = scope.ViewModel;
        var view = new CloudStorageView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        try
        {
            window.Show(); HeadlessTestHelpers.Render();
            var list = view.FindControl<ListBox>("StorageItems")!;
            RightClick(window, list.ContainerFromIndex(0)!);
            Assert.That(ActionIds(view), Is.EqualTo(new[] { "open", "rename", "move", "delete" }));
            view.StorageMenu!.Close();
            HeadlessTestHelpers.Settle();
            list.SelectedIndex = 1;
            list.ContainerFromIndex(1)!.Focus();
            window.KeyPressQwerty(PhysicalKey.F10, RawInputModifiers.Shift);
            window.KeyReleaseQwerty(PhysicalKey.F10, RawInputModifiers.Shift);
            HeadlessTestHelpers.Settle();
            Assert.That(view.StorageMenu?.IsOpen, Is.True);
            Assert.That(ActionIds(view), Does.Contain("download"));
            view.StorageMenu!.Close();
            window.MouseDown(new Point(400, 400), MouseButton.Right);
            window.MouseUp(new Point(400, 400), MouseButton.Right);
            HeadlessTestHelpers.Settle();
            Assert.That(ActionIds(view), Is.EqualTo(new[] { "createFolder", "refresh" }));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task EmptyFolderContextMenuCreatesInsideThatFolderAndRefreshesTheListing()
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response());
        var navigate = vm.OpenFolderAsync(vm.Items[0]);
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        scope.Handler.Requests[1].Complete(Response(folder: "folder & 日本", empty: true));
        await navigate;
        var view = new CloudStorageView { DataContext = vm, NamePrompt = (_, _, _) => Task.FromResult<string?>("  New folder  ") };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        try
        {
            window.Show(); HeadlessTestHelpers.Render();
            window.MouseDown(new Point(300, 260), MouseButton.Right);
            window.MouseUp(new Point(300, 260), MouseButton.Right);
            HeadlessTestHelpers.Settle();
            Assert.That(view.StorageMenu?.IsOpen, Is.True);
            ClickAction(view, "createFolder");
            await WaitFor(() => scope.Handler.Requests.Count == 3);
            var request = scope.Handler.Requests[2];
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.Uri.AbsolutePath, Is.EqualTo("/api/v3/storage/folders"));
            using var body = JsonDocument.Parse(await request.ReadBodyAsync());
            Assert.That(body.RootElement.GetProperty("name").GetString(), Is.EqualTo("New folder"));
            Assert.That(body.RootElement.GetProperty("parentId").GetString(), Is.EqualTo("folder & 日本"));
            request.Complete("{\"id\":\"created\"}", HttpStatusCode.Created);
            await WaitFor(() => scope.Handler.Requests.Count == 4);
            scope.Handler.Requests[3].Complete(Response(folder: "folder & 日本", fileId: "fresh"));
            await WaitFor(() => !vm.IsLoading.Value && !vm.IsBusy.Value);
            Assert.That(vm.Items.Single().Id, Is.EqualTo("fresh"));
            Assert.That(scope.Handler.UsageAuthorizations, Has.Count.EqualTo(2));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task AccountChangeDuringTheNamePromptCannotRenameForTheNextAccount()
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response());
        var prompt = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new CloudStorageView { DataContext = vm, NamePrompt = (_, _, _) => prompt.Task };
        var context = vm.CaptureActionContext([vm.Items[1]])!;
        var rename = view.ExecuteStorageActionAsync("rename", context);
        SignIn(scope.Clients, "b");
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        prompt.SetResult("wrong-account.mp4");
        await rename;
        Assert.That(scope.Handler.Requests.All(x => x.Method == HttpMethod.Get), Is.True);
        scope.Handler.Requests[1].Complete(Response());
        await WaitFor(() => !vm.IsLoading.Value);
    }

    [AvaloniaTest]
    [TestCase("rename", "PATCH", "/api/v3/storage/files/file")]
    [TestCase("move", "POST", "/api/v3/storage/files/batch")]
    [TestCase("setPublic", "POST", "/api/v3/storage/files/batch")]
    [TestCase("delete", "POST", "/api/v3/storage/files/batch")]
    public async Task FileOperationsUseTheCapturedOwnerAndRefreshAfterCompletion(string action, string method, string path)
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response());
        var context = vm.CaptureActionContext([vm.Items[1]])!;
        var operation = action switch
        {
            "rename" => vm.RenameAsync(context, "renamed.mp4"),
            "move" => vm.MoveAsync(context, "folder & 日本"),
            "setPublic" => vm.SetVisibilityAsync(context, true),
            _ => vm.DeleteAsync(context),
        };
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        var request = scope.Handler.Requests[1];
        Assert.That(request.Method.Method, Is.EqualTo(method));
        Assert.That(request.Uri.AbsolutePath, Is.EqualTo(path));
        Assert.That(request.Authorization, Is.EqualTo("Bearer token-a"));
        Assert.That(vm.IsBusy.Value, Is.True);
        request.Complete("{\"affected\":1}", action == "rename" ? HttpStatusCode.NoContent : HttpStatusCode.OK);
        await WaitFor(() => scope.Handler.Requests.Count == 3);
        scope.Handler.Requests[2].Complete(Response(fileId: "updated"));
        Assert.That(await operation, Is.True);
        Assert.That(vm.Items.Last().Id, Is.EqualTo("updated"));
        Assert.That(vm.IsBusy.Value, Is.False);
    }

    [AvaloniaTest]
    [TestCase("rename", "PATCH")]
    [TestCase("move", "PATCH")]
    [TestCase("delete", "DELETE")]
    public async Task FolderOperationsUseResourcePathsAndExplicitRecursiveDeletion(string action, string method)
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response());
        var context = vm.CaptureActionContext([vm.Items[0]])!;
        var operation = action switch
        {
            "rename" => vm.RenameAsync(context, "renamed"),
            "move" => vm.MoveAsync(context, null),
            _ => vm.DeleteAsync(context),
        };
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        var request = scope.Handler.Requests[1];
        Assert.That(request.Method.Method, Is.EqualTo(method));
        Assert.That(Uri.UnescapeDataString(request.Uri.AbsolutePath), Is.EqualTo("/api/v3/storage/folders/folder & 日本"));
        if (action == "delete") Assert.That(request.Uri.Query, Does.Contain("recursive=true"));
        else
        {
            using var body = JsonDocument.Parse(await request.ReadBodyAsync());
            if (action == "move") Assert.That(body.RootElement.GetProperty("parentId").ValueKind, Is.EqualTo(JsonValueKind.Null));
            else Assert.That(body.RootElement.GetProperty("name").GetString(), Is.EqualTo("renamed"));
        }
        request.Complete("{\"deletedFolders\":1,\"deletedFiles\":0}", action == "delete" ? HttpStatusCode.OK : HttpStatusCode.NoContent);
        await WaitFor(() => scope.Handler.Requests.Count == 3);
        scope.Handler.Requests[2].Complete(Response(empty: true));
        Assert.That(await operation, Is.True);
    }

    [AvaloniaTest]
    public async Task FolderDeletionRequiresConfirmationAndReportsServerCounts()
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response());
        string? confirmation = null;
        var view = new CloudStorageView { DataContext = vm, DeletePrompt = text => { confirmation = text; return Task.FromResult(false); } };
        var context = vm.CaptureActionContext([vm.Items[0]])!;
        var deletion = view.ExecuteStorageActionAsync("delete", context);
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        scope.Handler.Requests[1].Complete("{\"folder\":{\"id\":\"folder & 日本\",\"name\":\"素材\",\"parentId\":null},\"ancestors\":[],\"folderCount\":2,\"fileCount\":5}");
        await deletion;
        Assert.That(confirmation, Does.Contain("素材").And.Contain("2").And.Contain("5"));
        Assert.That(scope.Handler.Requests.All(x => x.Method == HttpMethod.Get), Is.True);
    }

    [AvaloniaTest]
    public async Task MultipleFilesUseBatchActionsAndPreserveTheSelectionOnRightClick()
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response(fileCount: 2));
        var view = new CloudStorageView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        try
        {
            window.Show(); HeadlessTestHelpers.Render();
            var list = view.FindControl<ListBox>("StorageItems")!;
            list.SelectedItems!.Add(vm.Items[1]);
            list.SelectedItems.Add(vm.Items[2]);
            RightClick(window, list.ContainerFromIndex(1)!);
            Assert.That(list.SelectedItems, Has.Count.EqualTo(2));
            Assert.That(ActionIds(view), Is.EqualTo(new[] { "move", "setPublic", "delete" }));
            view.StorageMenu!.Close();
            HeadlessTestHelpers.Settle();
            RightClick(window, list.ContainerFromIndex(0)!);
            Assert.That(list.SelectedItems, Has.Count.EqualTo(1));
            Assert.That(list.SelectedItem, Is.SameAs(vm.Items[0]));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task NameDialogValidatesInputAndMovePickerCanChooseTheRoot()
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response());
        var navigation = vm.OpenFolderAsync(vm.Items[0]);
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        scope.Handler.Requests[1].Complete(Response(folder: "folder & 日本"));
        await navigation;
        var view = new CloudStorageView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        try
        {
            window.Show(); HeadlessTestHelpers.Render();
            var create = view.ExecuteStorageActionAsync("createFolder", vm.CaptureActionContext([])!);
            await WaitFor(() => view.StorageDialog != null);
            HeadlessTestHelpers.Render();
            var dialog = view.StorageDialog!;
            var input = ((StackPanel)dialog.Content!).Children.OfType<TextBox>().Single();
            Assert.That(dialog.IsPrimaryButtonEnabled, Is.False);
            input.Text = "valid";
            Assert.That(dialog.IsPrimaryButtonEnabled, Is.True);
            input.Text = "bad\tname";
            Assert.That(dialog.IsPrimaryButtonEnabled, Is.False);
            Capture(window, "name-dialog");
            dialog.Hide();
            await create;
            Assert.That(scope.Handler.Requests, Has.Count.EqualTo(2));

            var move = view.ExecuteStorageActionAsync("move", vm.CaptureActionContext([vm.Items[0]])!);
            await WaitFor(() => scope.Handler.Requests.Count == 3);
            Assert.That(scope.Handler.Requests[2].Uri.Query, Does.Contain("kind=folder"));
            scope.Handler.Requests[2].Complete(Response(folder: "folder & 日本", empty: true));
            await WaitFor(() => view.StorageDialog != null);
            HeadlessTestHelpers.Render();
            dialog = view.StorageDialog!;
            var home = ((StackPanel)dialog.Content!).Children.OfType<StackPanel>().Single().Children
                .OfType<Button>().Single(x => Equals(x.Content, Strings.Home));
            await WaitFor(() => home.IsEnabled);
            home.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => scope.Handler.Requests.Count == 4);
            scope.Handler.Requests[3].Complete(Response(empty: true));
            await WaitFor(() => dialog.IsPrimaryButtonEnabled);
            Capture(window, "move-dialog");
            dialog.Hide();
            await move;
            Assert.That(scope.Handler.Requests.All(x => x.Method == HttpMethod.Get), Is.True);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task StaleSelectionAndInvalidNamesCannotStartWritesAndRootMovesKeepExplicitNull()
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response());
        var file = vm.Items[1];
        var context = vm.CaptureActionContext([file])!;
        Assert.That(await vm.RenameAsync(context, "bad\nname"), Is.False);
        var move = vm.MoveAsync(context, null);
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        using var body = JsonDocument.Parse(await scope.Handler.Requests[1].ReadBodyAsync());
        Assert.That(body.RootElement.GetProperty("parentId").ValueKind, Is.EqualTo(JsonValueKind.Null));
        scope.Handler.Requests[1].Complete("{\"affected\":1}");
        await WaitFor(() => scope.Handler.Requests.Count == 3);
        scope.Handler.Requests[2].Complete(Response());
        Assert.That(await move, Is.True);
        Assert.That(await vm.DeleteAsync(context), Is.False);
        Assert.That(scope.Handler.Requests, Has.Count.EqualTo(3));
    }

    [AvaloniaTest]
    public async Task FailureIsReportedAndAccountSwitchCancelsAnInFlightWriteWithoutPublishingItsResult()
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response());
        var change = vm.SetVisibilityAsync(vm.CaptureActionContext([vm.Items[1]])!, true);
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        scope.Handler.Requests[1].Complete("{\"error_code\":\"storageFileInUse\"}", HttpStatusCode.Conflict);
        await WaitFor(() => scope.Handler.Requests.Count == 3);
        scope.Handler.Requests[2].Complete(Response());
        Assert.That(await change, Is.False);
        Assert.That(vm.ActionError.Value, Is.EqualTo(Strings.CloudStorageFileInUse));
        change = vm.RenameAsync(vm.CaptureActionContext([vm.Items[1]])!, "new.mp4");
        await WaitFor(() => scope.Handler.Requests.Count == 4);
        SignIn(scope.Clients, "b");
        await WaitFor(() => scope.Handler.Requests.Count == 5);
        Assert.That(scope.Handler.Requests[3].Token.IsCancellationRequested, Is.True);
        scope.Handler.Requests[3].Complete("{}", HttpStatusCode.NoContent);
        Assert.That(await change, Is.False);
        scope.Handler.Requests[4].Complete(Response(fileId: "account-b"));
        await WaitFor(() => !vm.IsLoading.Value);
        Assert.That(vm.ActionError.Value, Is.Null);
        Assert.That(vm.Items.Last().Id, Is.EqualTo("account-b"));
    }

    [AvaloniaTest]
    public async Task DownloadUsesTheAuthenticatedContentResourceAndUsageIsNotRepeatedDuringNavigation()
    {
        await using var scope = new StorageScope();
        var vm = scope.ViewModel;
        await scope.LoadFirstAsync(Response());
        var context = vm.CaptureActionContext([vm.Items[1]])!;
        using var output = new MemoryStream();
        var download = vm.DownloadAsync(context, output, CancellationToken.None);
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        var request = scope.Handler.Requests[1];
        Assert.That(request.Uri.AbsolutePath, Is.EqualTo("/api/v3/storage/files/file/content"));
        Assert.That(request.Authorization, Is.EqualTo("Bearer token-a"));
        request.Complete("downloaded content");
        Assert.That(await download, Is.True);
        Assert.That(System.Text.Encoding.UTF8.GetString(output.ToArray()), Is.EqualTo("downloaded content"));
        Assert.That(scope.Handler.UsageAuthorizations, Has.Count.EqualTo(1));
    }

    private static string[] ActionIds(CloudStorageView view) => view.StorageMenu!.Items.OfType<MenuItem>()
        .Where(x => x.Name?.StartsWith("StorageAction_", StringComparison.Ordinal) == true)
        .Select(x => x.Name!["StorageAction_".Length..]).ToArray();

    private static void Capture(Control control, string name)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_STORAGE_ACTION_CAPTURE") is not { Length: > 0 } path) return;
        HeadlessTestHelpers.Render();
        Directory.CreateDirectory(path);
        using var image = TopLevel.GetTopLevel(control)?.CaptureRenderedFrame();
        image?.Save(Path.Combine(path, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static void ClickAction(CloudStorageView view, string action) => view.StorageMenu!.Items.OfType<MenuItem>()
        .Single(x => x.Name == $"StorageAction_{action}").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    private static void RightClick(Window window, Control item)
    {
        var position = item.TranslatePoint(new Point(20, 20), window)!.Value;
        window.MouseMove(position);
        window.MouseDown(position, MouseButton.Right);
        window.MouseUp(position, MouseButton.Right);
        HeadlessTestHelpers.Settle();
    }
}
