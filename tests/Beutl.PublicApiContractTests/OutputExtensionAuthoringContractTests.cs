using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Beutl.Extensibility;
using Reactive.Bindings;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class OutputExtensionAuthoringContractTests
{
    [Test]
    public async Task ExternalExtension_CanImplementAndConsumeTheOutputExecutionContract()
    {
        var editor = new TestEditorContext();
        var extension = new TestOutputExtension();
        var execution = new TestOutputExecutionController();
        var outputOperations = new TestOutputOperationLeaseProvider();
        using var cancellation = new CancellationTokenSource();
        using IDisposable outputOperation = outputOperations.TryBeginOutputOperation()!;

        extension.TryCreateContext(editor, out IOutputContext? context);
        extension.TryCreateControl(
            editor,
            context!,
            execution,
            out _);
        await context!.RunAsync(cancellation.Token);
        execution.TryStart(out Task? executionTask);
        await executionTask!;
        execution.Cancel();
    }

    private sealed class TestOutputExtension : OutputExtension
    {
        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IOutputContext? context)
        {
            context = new TestOutputContext(this);
            return true;
        }

        public override bool TryCreateControl(
            IEditorContext editorContext,
            IOutputContext context,
            IOutputExecutionController execution,
            [NotNullWhen(true)] out Control? control)
        {
            control = new Border();
            return true;
        }

        public override bool IsSupported(Type type) => type == typeof(TestCoreObject);
    }

    private sealed class TestOutputContext(OutputExtension extension) : IOutputContext
    {
        public OutputExtension Extension { get; } = extension;

        public CoreObject Object { get; } = new TestCoreObject();

        public IReactiveProperty<string> Name { get; } = new ReactivePropertySlim<string>("Contract output");

        public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }

        public void WriteToJson(JsonObject json)
        {
        }

        public void ReadFromJson(JsonObject json)
        {
        }
    }

    private sealed class TestOutputExecutionController : IOutputExecutionController
    {
        public IReadOnlyReactiveProperty<bool> IsRunning { get; }
            = new ReactivePropertySlim<bool>();

        public bool TryStart([NotNullWhen(true)] out Task? execution)
        {
            execution = Task.CompletedTask;
            return true;
        }

        public void Cancel()
        {
        }
    }

    private sealed class TestOutputOperationLeaseProvider : IOutputOperationLeaseProvider
    {
        public IDisposable TryBeginOutputOperation()
            => StandaloneOutputOperationLeaseProvider.Instance.TryBeginOutputOperation();
    }

    private sealed class TestEditorContext : IEditorContext
    {
        public CoreObject Object { get; } = new TestCoreObject();

        public EditorExtension Extension => null!;

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

        public T? FindToolTab<T>(Func<T, bool> condition)
            where T : IToolContext
            => default;

        public T? FindToolTab<T>()
            where T : IToolContext
            => default;

        public bool OpenToolTab(IToolContext item) => false;

        public void CloseToolTab(IToolContext item)
        {
        }

        public object? GetService(Type serviceType) => null;
    }

    private sealed class TestCoreObject : CoreObject;
}
