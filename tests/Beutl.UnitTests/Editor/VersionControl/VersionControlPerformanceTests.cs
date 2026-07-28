using System.Diagnostics;
using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class VersionControlPerformanceTests : RealGitTestRepository
{
    private static readonly TimeSpan s_snapshotLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan s_historyLimit = TimeSpan.FromSeconds(5);

    [Test]
    public async Task Snapshot_of_500_element_project_completes_within_bound()
    {
        const int elementCount = 500;
        await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), """{"name":"Performance fixture"}""" + "\n");
        await File.WriteAllTextAsync(Path.Combine(Root, "main.scene"), """{"elements":[]}""" + "\n");
        await RunGitAsync("add", "-A", "--", ".");
        await RunGitAsync("commit", "-m", "project baseline");

        string elementsDirectory = Path.Combine(Root, "elements");
        Directory.CreateDirectory(elementsDirectory);
        var elementNames = new string[elementCount];
        for (int index = 0; index < elementCount; index++)
        {
            string id = (index + 1).ToString("x32");
            string fileName = $"{id}.belm";
            elementNames[index] = fileName;
            await File.WriteAllTextAsync(
                Path.Combine(elementsDirectory, fileName),
                $"{{\"id\":\"{id}\",\"opacity\":1.0,\"position\":{{\"x\":{index},\"y\":{index}}}}}\n");
        }

        string elementReferences = string.Join(
            ',',
            elementNames.Select(static name => $"\"elements/{name}\""));
        await File.WriteAllTextAsync(
            Path.Combine(Root, "main.scene"),
            $"{{\"elements\":[{elementReferences}]}}\n");
        using var service = CreateService();

        var stopwatch = Stopwatch.StartNew();
        CommitResult result = await service.CommitAllAsync(
            "beutl: snapshot on save",
            SnapshotKind.Save,
            CancellationToken.None);
        stopwatch.Stop();
        TestContext.Progress.WriteLine(
            $"SC-003 snapshot of {elementCount} elements: {stopwatch.Elapsed.TotalMilliseconds:F1} ms");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<CommitResult.Committed>());
            Assert.That(
                stopwatch.Elapsed,
                Is.LessThan(s_snapshotLimit),
                $"A {elementCount}-element snapshot took {stopwatch.Elapsed.TotalMilliseconds:F1} ms.");
        });
    }

    [Test]
    public async Task Loading_200_commit_history_completes_within_bound()
    {
        const int requestedCommitCount = 200;
        await CommitFileAsync("project.bep", "0\n", "history baseline");
        for (int index = 1; index <= requestedCommitCount; index++)
        {
            await File.WriteAllTextAsync(Path.Combine(Root, "project.bep"), $"{index}\n");
            await RunGitAsync("commit", "-am", $"history {index}");
        }

        using var service = CreateService();
        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<CommitInfo> history = await service.GetHistoryAsync(
            0,
            requestedCommitCount,
            CancellationToken.None);
        stopwatch.Stop();
        TestContext.Progress.WriteLine(
            $"SC-003 history load of {requestedCommitCount} commits: {stopwatch.Elapsed.TotalMilliseconds:F1} ms");

        Assert.Multiple(() =>
        {
            Assert.That(history, Has.Count.EqualTo(requestedCommitCount));
            Assert.That(
                stopwatch.Elapsed,
                Is.LessThan(s_historyLimit),
                $"Loading {requestedCommitCount} commits took {stopwatch.Elapsed.TotalMilliseconds:F1} ms.");
        });
    }

    private GitCliVersionControlService CreateService()
    {
        return new GitCliVersionControlService(
            CreateInstalledLocator(),
            Repository,
            watcher: null,
            _ => CreateRunner(TimeSpan.FromSeconds(30)));
    }
}
