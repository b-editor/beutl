using System.Net;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.PackageTools.UI.Models;
using Beutl.PackageTools.UI.ViewModels;
using Beutl.UnitTests.TestInfrastructure;
using NuGet.Packaging.Core;
using NuGet.Versioning;

using PackageToolsMainViewModel = Beutl.PackageTools.UI.ViewModels.MainViewModel;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public sealed class PackageChangesQueueTests
{
    private static readonly PackageIdentity s_install = new("Queued.Install", NuGetVersion.Parse("1.0.0"));
    private static readonly PackageIdentity s_uninstall = new("Queued.Uninstall", NuGetVersion.Parse("2.0.0"));
    private string _path = null!;

    [SetUp]
    public void SetUp() => _path = Path.Combine(Path.GetTempPath(), $"packageChanges-{Guid.NewGuid():N}.json");

    [TearDown]
    public void TearDown() => File.Delete(_path);

    [Test]
    public void A_new_queue_reads_the_changes_the_last_one_saved()
    {
        var queue = new PackageChangesQueue(_path);
        queue.InstallQueue(s_install);
        queue.UninstallQueue(s_uninstall);
        queue.InstallQueue(new PackageIdentity("Queued.Canceled", NuGetVersion.Parse("1.0.0")));
        queue.Cancel("Queued.Canceled");

        var restored = new PackageChangesQueue(_path);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored.GetInstalls(), Is.EqualTo(new[] { s_install }));
            Assert.That(restored.GetUninstalls(), Is.EqualTo(new[] { s_uninstall }));
        }
    }

    [TestCase(null)]
    [TestCase("not json")]
    [TestCase("""{"Installs":["Queued.Install/1.0.0"],"Uninstalls":[]}""")]
    [TestCase("""{"Installs":[{"Name":"Queued.Install","Version":"not a version"}],"Uninstalls":[]}""")]
    public void A_missing_old_or_corrupt_file_reads_as_empty(string? contents)
    {
        if (contents is not null)
            File.WriteAllText(_path, contents);

        var queue = new PackageChangesQueue(_path);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(queue.GetInstalls(), Is.Empty);
            Assert.That(queue.GetUninstalls(), Is.Empty);
        }
    }

    [Test]
    public async Task PackageTools_lists_the_queued_changes()
    {
        string path = Path.Combine(Helper.AppRoot, "packageChanges.json");
        byte[]? original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        try
        {
            new PackageChangesQueue(path).UninstallQueue(s_uninstall);
            using var handler = new DelegateHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
            using var httpClient = new HttpClient(handler);
            await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
            var viewModel = new PackageToolsMainViewModel(
                httpClient, app, new ChangesModel(), [], [], disposeResources: () => ValueTask.CompletedTask);

            await viewModel.InitializationTask.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.That(
                viewModel.UninstallItems.Select(item => new PackageIdentity(item.Id, item.Version)),
                Is.EqualTo(new[] { s_uninstall }));
            await viewModel.DisposeAsync();
        }
        finally
        {
            if (original is null)
                File.Delete(path);
            else
                File.WriteAllBytes(path, original);
        }
    }

    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(false, false, true)]
    public void PackageTools_removes_a_change_once_it_ran_whatever_the_result(bool succeeded, bool failed, bool canceled)
    {
        var queue = new PackageChangesQueue(_path);
        queue.UninstallQueue(s_uninstall);
        var model = new PackageChangeModel(
            s_uninstall.Id, s_uninstall.Version, s_uninstall.Id, false, PackageChangeAction.Uninstall);
        var viewModel = new ActionViewModel(new ChangesModel(), model);
        PackageToolsMainViewModel.RemoveOnceRun(viewModel, queue);

        viewModel.Succeeded.Value = succeeded;
        viewModel.Failed.Value = failed;
        viewModel.Canceled.Value = canceled;

        Assert.That(
            new PackageChangesQueue(_path).GetUninstalls(),
            canceled ? Is.EqualTo(new[] { s_uninstall }) : Is.Empty);
    }
}
