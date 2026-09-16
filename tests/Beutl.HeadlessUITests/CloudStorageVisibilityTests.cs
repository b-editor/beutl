using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Beutl.Testing.Headless;
using Beutl.Views.Tools;
using static Beutl.HeadlessUITests.CloudStorageTests;
using StorageScope = Beutl.HeadlessUITests.CloudStorageIncrementalTests.StorageScope;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class CloudStorageVisibilityTests
{
    [AvaloniaTest]
    [TestCase("PRIVATE", "PUBLIC")]
    [TestCase("PRIVATE", "DEDICATED")]
    [TestCase("PUBLIC", "DEDICATED")]
    public async Task MixedPermissionsDoNotOfferPartialVisibilityActions(string firstVisibility, string secondVisibility)
    {
        await using var scope = new StorageScope();
        var response = JsonNode.Parse(Response(visibility: firstVisibility))!;
        response["entries"]!.AsArray().Add(JsonNode.Parse(Response(fileId: "second", visibility: secondVisibility))!["entries"]![1]!.DeepClone());
        await scope.LoadFirstAsync(response.ToJsonString());
        var vm = scope.ViewModel;
        var view = new CloudStorageView { DataContext = vm };
        var window = new Window { Content = view, Width = 640, Height = 520 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            var list = view.FindControl<ListBox>("StorageItems")!;
            list.SelectedItems!.Add(vm.Items[1]);
            list.SelectedItems.Add(vm.Items[2]);
            var point = list.ContainerFromIndex(1)!.TranslatePoint(new Point(20, 20), window)!.Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            HeadlessTestHelpers.Settle();
            var names = view.StorageMenu!.Items.OfType<MenuItem>().Select(item => item.Name).ToArray();
            Assert.That(names, Does.Not.Contain("StorageAction_setPublic").And.Not.Contain("StorageAction_setPrivate"));

            var context = vm.CaptureActionContext([vm.Items[1], vm.Items[2]])!;
            foreach (bool makePublic in new[] { true, false })
            {
                var operation = vm.SetVisibilityAsync(context, makePublic);
                Assert.That(operation.IsCompletedSuccessfully, Is.True, "An unsupported selection must be rejected before any request.");
                Assert.That(await operation, Is.False);
            }
            Assert.That(scope.Handler.Requests, Has.Count.EqualTo(1));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public async Task VisibilityUpdatesIncludeEverySelectedFile(bool makePublic)
    {
        await using var scope = new StorageScope();
        await scope.LoadFirstAsync(Response(fileCount: 2, visibility: makePublic ? "PRIVATE" : "PUBLIC"));
        var vm = scope.ViewModel;
        var items = vm.Items.Skip(1).ToArray();
        var operation = vm.SetVisibilityAsync(vm.CaptureActionContext(items)!, makePublic);
        await WaitFor(() => scope.Handler.Requests.Count == 2);
        using var body = JsonDocument.Parse(await scope.Handler.Requests[1].ReadBodyAsync());
        Assert.That(body.RootElement.GetProperty("ids").EnumerateArray().Select(x => x.GetString()), Is.EqualTo(items.Select(x => x.Id)));
        Assert.That(body.RootElement.GetProperty("visibility").GetString(), Is.EqualTo(makePublic ? "PUBLIC" : "PRIVATE"));
        scope.Handler.Requests[1].Complete("{\"affected\":2}");
        await WaitFor(() => scope.Handler.Requests.Count == 3);
        scope.Handler.Requests[2].Complete(Response(fileCount: 2, visibility: makePublic ? "PUBLIC" : "PRIVATE"));
        Assert.That(await operation, Is.True);
    }
}
