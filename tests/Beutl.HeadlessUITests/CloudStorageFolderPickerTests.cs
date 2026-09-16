using System.Net;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Api.Clients;
using Beutl.Testing.Headless;
using Beutl.Views.Tools;
using static Beutl.HeadlessUITests.CloudStorageTests;
using StorageScope = Beutl.HeadlessUITests.CloudStorageIncrementalTests.StorageScope;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class CloudStorageFolderPickerTests
{
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
            var response = JsonNode.Parse(Response(empty: true))!;
            response["entries"] = new JsonArray(Enumerable.Range(0, 24).Select(index => (JsonNode)new JsonObject
            {
                ["id"] = $"folder-{index}",
                ["kind"] = "folder",
                ["name"] = $"Folder {index}",
            }).ToArray());
            response["nextCursor"] = "folders-next";
            scope.Handler.Requests[1].Complete(response.ToJsonString());
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
