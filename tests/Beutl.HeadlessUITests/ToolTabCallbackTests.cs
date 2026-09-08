using System.Reflection;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ToolTabCallbackTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Rejected_factory_result_is_cleaned_without_disposing_a_foreign_tool(bool throws)
    {
        await TestReset.ResetShellAsync();
        var editor = new FailingEditorContext();
        var tool = new RejectedTool();
        var extension = new RejectingExtension(tool, throws);
        var owner = new ToolContextHostToken();
        Assert.That(owner.TryAcquireContext(tool, out var lease), Is.True);
        using (lease)
        {
            await ToolTabCallback.OpenFromExtensionAsync(editor, extension);
            Assert.That(tool.DisposeCalls, Is.Zero);
        }
        await ToolTabCallback.OpenFromExtensionAsync(editor, extension);
        Assert.That(tool.DisposeCalls, Is.EqualTo(1));
    }
    [AvaloniaTest]
    public async Task Both_synchronous_and_asynchronous_callback_failures_are_contained()
    {
        await TestReset.ResetShellAsync();
        await ToolTabCallback.RunAsync(() => throw new InvalidOperationException("synchronous callback"));
        await ToolTabCallback.RunAsync(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("asynchronous callback");
        });
    }

    [AvaloniaTest]
    [TestCase("OnElementDetached")]
    [TestCase("OnAnimationDetached")]
    public async Task Graph_detach_callback_contains_asynchronous_close_failure(string methodName)
    {
        await TestReset.ResetShellAsync();
        var editor = new FailingEditorContext();
        var graph = new GraphEditorTabViewModel(editor);
        try
        {
            typeof(GraphEditorTabViewModel).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(graph, [null, null]);
            await editor.CloseStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Yield();
            HeadlessTestHelpers.Settle();
            Assert.That(editor.CloseCalls, Is.EqualTo(1));
        }
        finally
        {
            await graph.DisposeAsync();
        }
    }

    [AvaloniaTest]
    public async Task Library_disposal_drains_search_admission_and_rejects_late_searches()
    {
        await TestReset.ResetShellAsync();
        var library = new LibraryTabViewModel(new FailingEditorContext());
        var gate = (SemaphoreSlim)typeof(LibraryTabViewModel)
            .GetField("_asyncLock", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(library)!;
        await gate.WaitAsync();
        Task search = library.Search("rectangle", CancellationToken.None);
        Task disposal = library.DisposeAsync().AsTask();
        Assert.That(disposal.IsCompleted, Is.False);
        gate.Release();
        await Task.WhenAll(search, disposal).WaitAsync(TimeSpan.FromSeconds(5));
        await library.Search("rectangle", CancellationToken.None);
        Assert.That(library.AllItems, Is.Empty);
        Assert.That(library.SearchResult, Is.Empty);
    }

    private sealed class FailingEditorContext : IEditorContext
    {
        public TaskCompletionSource CloseStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CloseCalls { get; private set; }
        public CoreObject Object { get; } = new Scene();
        public EditorExtension Extension => throw new NotSupportedException();
        public IEditorContextCloseService CloseService => TestShell.Editor;
        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);
        public IKnownEditorCommands? Commands => null;
        public object? GetService(Type serviceType) => null;
        public T? FindToolTab<T>() where T : IToolContext => default;
        public T? FindToolTab<T>(Func<T, bool> condition) where T : IToolContext => default;
        public ValueTask<bool> OpenToolTabAsync(IToolContext item) => ValueTask.FromResult(false);
        public async ValueTask CloseToolTabAsync(IToolContext item)
        {
            CloseCalls++;
            CloseStarted.TrySetResult();
            await Task.Yield();
            throw new InvalidOperationException("close callback");
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RejectedTool : IToolContext
    {
        public int DisposeCalls { get; private set; }
        public ToolTabExtension Extension => throw new NotSupportedException();
        public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();
        public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>("Rejected");
        public ValueTask DisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
        public object? GetService(Type serviceType) => null;
        public void ReadFromJson(System.Text.Json.Nodes.JsonObject json) { }
        public void WriteToJson(System.Text.Json.Nodes.JsonObject json) { }
    }

    private sealed class RejectingExtension(IToolContext tool, bool throws) : ToolTabExtension
    {
        public override bool CanMultiple => true;
        public override bool TryCreateContext(IEditorContext editor,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IToolContext? context)
        {
            context = tool;
            if (throws)
                throw new InvalidOperationException("factory failed after allocation");
            return false;
        }
        public override bool TryCreateContent(IEditorContext editor,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Avalonia.Controls.Control? control)
        {
            control = null;
            return false;
        }
    }
}
