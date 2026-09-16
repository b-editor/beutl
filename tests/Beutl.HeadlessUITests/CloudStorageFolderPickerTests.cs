using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Api.Clients;
using Beutl.Language;
using Beutl.Testing.Headless;
using Beutl.Views.Tools;
using static Beutl.HeadlessUITests.CloudStorageTests;
using StorageScope = Beutl.HeadlessUITests.CloudStorageIncrementalTests.StorageScope;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class CloudStorageFolderPickerTests
{
    [AvaloniaTest]
    public async Task OptionalPageFailureDoesNotBlockMovingToTheValidatedDestination()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response());
        var vm = scope.ViewModel;
        var navigation = vm.OpenFolderAsync(vm.Items[0]);
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        scope.Handler.Requests[1].Complete(Response(folder: "folder & 日本"));
        await navigation;
        var view = new CloudStorageView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            var move = view.ExecuteStorageActionAsync("move", vm.CaptureActionContext([vm.Items.Single()])!);
            await WaitFor(() => scope.Handler.Requests.Count == 3);
            scope.Handler.Requests[2].Complete(Response(folder: "folder & 日本", empty: true));
            var dialog = view.StorageDialog!;
            var content = (StackPanel)dialog.Content!;
            var home = content.Children.OfType<StackPanel>().Single().Children.OfType<Button>()
                .Single(button => Equals(button.Content, Strings.Home));
            await WaitFor(() => home.IsEnabled);
            home.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => scope.Handler.Requests.Count == 4);
            scope.Handler.Requests[3].Complete(DestinationPage());
            var list = content.Children.OfType<ListBox>().Single();
            await WaitFor(() => list.Items.Count == 24 && dialog.IsPrimaryButtonEnabled);
            HeadlessTestHelpers.Render();
            list.GetVisualDescendants().OfType<ScrollViewer>().Single().ScrollToEnd();
            HeadlessTestHelpers.Render();
            await WaitFor(() => scope.Handler.Requests.Count == 5);
            scope.Handler.Requests[4].Complete("{}", HttpStatusCode.ServiceUnavailable);
            var retry = content.Children.OfType<Button>().Single(button => button.Name == "StorageFolderRetry");
            await WaitFor(() => retry.IsVisible);
            Assert.That(dialog.IsPrimaryButtonEnabled, Is.True, "An optional child page must not invalidate the root destination.");

            dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PrimaryButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => scope.Handler.Requests.Count == 6);
            var request = scope.Handler.Requests[5];
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.Uri.AbsolutePath, Is.EqualTo("/api/v3/storage/files/batch"));
            using var body = JsonDocument.Parse(await request.ReadBodyAsync());
            Assert.That(body.RootElement.GetProperty("parentId").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(body.RootElement.GetProperty("ids").EnumerateArray().Select(id => id.GetString()), Is.EqualTo(new[] { "file" }));
            request.Complete("{\"affected\":1}");
            await WaitFor(() => scope.Handler.Requests.Count == 7);
            scope.Handler.Requests[6].Complete(Response(folder: "folder & 日本", empty: true));
            await move;
            Assert.That(vm.ActionError.Value, Is.Null);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task FailedNextPageRetriesTheSameCursorAndPreservesLoadedChoices()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response());
        var vm = scope.ViewModel;
        var view = new CloudStorageView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            var move = view.ExecuteStorageActionAsync("move", vm.CaptureActionContext([vm.Items[1]])!);
            await WaitFor(() => scope.Handler.Requests.Count == 2);
            scope.Handler.Requests[1].Complete(DestinationPage());
            var dialog = view.StorageDialog!;
            var content = (StackPanel)dialog.Content!;
            var list = content.Children.OfType<ListBox>().Single();
            await WaitFor(() => list.Items.Count == 24 && list.IsEnabled);
            HeadlessTestHelpers.Render();
            list.GetVisualDescendants().OfType<ScrollViewer>().Single().ScrollToEnd();
            HeadlessTestHelpers.Render();
            await WaitFor(() => scope.Handler.Requests.Count == 3);
            Assert.That(scope.Handler.Requests[2].Uri.Query, Does.Contain("cursor=folders-next"));
            scope.Handler.Requests[2].Complete("{}", HttpStatusCode.ServiceUnavailable);
            var retry = content.Children.OfType<Button>().Single(x => x.Name == "StorageFolderRetry");
            await WaitFor(() => retry.IsVisible);
            Assert.That(list.Items, Has.Count.EqualTo(24));
            Assert.That(dialog.IsPrimaryButtonEnabled, Is.False, "Pagination must not make the original folder a valid destination.");
            HeadlessTestHelpers.Render();
            Assert.That(scope.Handler.Requests, Has.Count.EqualTo(3));

            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => scope.Handler.Requests.Count == 4);
            Assert.That(scope.Handler.Requests[3].Uri, Is.EqualTo(scope.Handler.Requests[2].Uri));
            scope.Handler.Requests[3].Complete(Response());
            await WaitFor(() => list.Items.Count == 25 && list.IsEnabled);
            Assert.That(retry.IsVisible, Is.False);
            Assert.That(view.StorageDialog, Is.SameAs(dialog));
            dialog.Hide();
            await move;
        }
        finally { window.Close(); }
    }

    private static string DestinationPage()
    {
        var response = JsonNode.Parse(Response(empty: true))!;
        response["entries"] = new JsonArray(Enumerable.Range(0, 24).Select(index => (JsonNode)new JsonObject
        {
            ["id"] = $"folder-{index}",
            ["kind"] = "folder",
            ["name"] = $"Folder {index}",
        }).ToArray());
        response["nextCursor"] = "folders-next";
        return response.ToJsonString();
    }

    [AvaloniaTest]
    public async Task FailedRootListingCanBeRetriedWithoutClosingTheMoveDialog()
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response());
        var vm = scope.ViewModel;
        var view = new CloudStorageView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            var move = view.ExecuteStorageActionAsync("move", vm.CaptureActionContext([vm.Items[1]])!);
            await WaitFor(() => scope.Handler.Requests.Count == 2);
            scope.Handler.Requests[1].Complete("{}", HttpStatusCode.ServiceUnavailable);
            var dialog = view.StorageDialog!;
            var content = (StackPanel)dialog.Content!;
            await WaitFor(() => !content.Children.OfType<ProgressBar>().Single().IsVisible);
            var retry = content.Children.OfType<Button>().SingleOrDefault(x => x.Name == "StorageFolderRetry");
            Assert.That(retry, Is.Not.Null, "A failed root load must leave a retry action in the open dialog.");
            Assert.That(retry!.IsVisible, Is.True);
            Assert.That(dialog.IsPrimaryButtonEnabled, Is.False);

            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => scope.Handler.Requests.Count == 3);
            Assert.That(scope.Handler.Requests[2].Uri, Is.EqualTo(scope.Handler.Requests[1].Uri));
            Assert.That(retry.IsVisible, Is.False);
            scope.Handler.Requests[2].Complete(Response());
            var list = content.Children.OfType<ListBox>().Single();
            await WaitFor(() => list.Items.Count == 1 && list.IsEnabled);
            Assert.That(list.Items.OfType<StorageEntryResponse>().Single().Id, Is.EqualTo("folder & 日本"));
            Assert.That(view.StorageDialog, Is.SameAs(dialog));
            Assert.That(dialog.IsPrimaryButtonEnabled, Is.False, "Reloading must not permit a move to the original destination.");
            Assert.That(scope.Handler.Requests.All(x => x.Method == HttpMethod.Get), Is.True);
            dialog.Hide();
            await move;
        }
        finally { window.Close(); }
    }
}
