using System.Text.Json.Nodes;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using PackageSample;
using Reactive.Bindings;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public class SampleToolOwnershipTests
{
    [Test]
    public async Task Sample_host_preserves_foreign_ownership_and_holds_its_lease_until_disposal()
    {
        string path = Path.GetTempFileName();
        try
        {
            var scene = new Scene { Uri = new Uri(path) };
            await using var editor = new TextEditorContext(scene, new SampleEditorExtension(), new CloseService());
            var tool = new BlockingTool();
            var foreign = new ToolContextHostToken();
            Assert.That(foreign.TryAcquireContext(tool, out ToolContextOwnershipLease? lease), Is.True);
            using (lease)
            {
                Assert.That(await editor.OpenToolTabAsync(tool), Is.False);
                Assert.That(tool.DisposeCalls, Is.Zero);
            }

            Task<bool> opening = editor.OpenToolTabAsync(tool).AsTask();
            Assert.That(tool.DisposeCalls, Is.EqualTo(1));
            Assert.That(foreign.TryAcquireContext(tool, out _), Is.False);
            tool.Release.TrySetResult();
            Assert.That(await opening, Is.False);
            Assert.That(foreign.TryAcquireContext(tool, out lease), Is.True);
            lease!.Dispose();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class CloseService : IEditorContextCloseService
    {
        public EditorContextHostToken HostToken { get; } = new();
        public EditorContextCloseRequest RequestClose(IEditorContext context)
            => new(EditorContextCloseRequestStatus.NotOwned, Task.CompletedTask);
    }

    private sealed class BlockingTool : IToolContext
    {
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCalls { get; private set; }
        public ToolTabExtension Extension => throw new NotSupportedException();
        public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();
        public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>("Test");
        public async ValueTask DisposeAsync() { DisposeCalls++; await Release.Task; }
        public object? GetService(Type serviceType) => null;
        public void ReadFromJson(JsonObject json) { }
        public void WriteToJson(JsonObject json) { }
    }
}
