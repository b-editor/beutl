using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Extensibility;
using Reactive.Bindings;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class OutputExtensionAuthoringContractTests : PublicApiContractTestBase
{
    [Test]
    public async Task ExternalExtension_CanImplementAndConsumeTheOutputExecutionContract()
    {
        AssertDoesNotHaveFriendAccess(typeof(OutputExtension).Assembly);

        await using var editor = new TestEditorContext();
        var extension = new TestOutputExtension();
        var execution = new TestOutputExecutionController();
        var outputOperations = new TestOutputOperationLeaseProvider();
        using var cancellation = new CancellationTokenSource();
        using IDisposable outputOperation = outputOperations.TryBeginOutputOperation()!;

        bool contextCreated = extension.TryCreateContext(editor, out IOutputContext? context);
        Assert.That(contextCreated, Is.True);
        Assert.That(context, Is.Not.Null);

        bool controlCreated = extension.TryCreateControl(
            editor,
            context!,
            execution,
            out Control? control);
        await context!.RunAsync(cancellation.Token);
        bool executionStarted = execution.TryStart(out Task? executionTask);
        await executionTask!;
        execution.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(controlCreated, Is.True);
            Assert.That(control, Is.TypeOf<Border>());
            Assert.That(extension.ContextEditor, Is.SameAs(editor));
            Assert.That(extension.ControlEditor, Is.SameAs(editor));
            Assert.That(extension.ControlContext, Is.SameAs(context));
            Assert.That(extension.ControlExecution, Is.SameAs(execution));
            Assert.That(executionStarted, Is.True);
            Assert.That(((TestOutputContext)context).LastRunToken, Is.EqualTo(cancellation.Token));
            Assert.That(execution.CancelCalls, Is.EqualTo(1));
            Assert.That(outputOperations.AcquireCalls, Is.EqualTo(1));
        });
    }

    private sealed class TestOutputExtension : OutputExtension
    {
        public IEditorContext? ContextEditor { get; private set; }

        public IEditorContext? ControlEditor { get; private set; }

        public IOutputContext? ControlContext { get; private set; }

        public IOutputExecutionController? ControlExecution { get; private set; }

        public override FilePickerFileType GetFilePickerFileType()
            => new("Contract output") { Patterns = ["*.contract"] };

        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IOutputContext? context)
        {
            ContextEditor = editorContext;
            context = new TestOutputContext(this);
            return true;
        }

        public override bool TryCreateControl(
            IEditorContext editorContext,
            IOutputContext context,
            IOutputExecutionController execution,
            [NotNullWhen(true)] out Control? control)
        {
            ControlEditor = editorContext;
            ControlContext = context;
            ControlExecution = execution;
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

        public IReadOnlyReactiveProperty<bool> IsIndeterminate { get; }
            = new ReactivePropertySlim<bool>();

        public IReadOnlyReactiveProperty<double> Progress { get; }
            = new ReactivePropertySlim<double>();

        public CancellationToken LastRunToken { get; private set; }

        public Task RunAsync(CancellationToken cancellationToken)
        {
            LastRunToken = cancellationToken;
            return Task.CompletedTask;
        }

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

        public int CancelCalls { get; private set; }

        public bool TryStart([NotNullWhen(true)] out Task? execution)
        {
            execution = Task.CompletedTask;
            return true;
        }

        public void Cancel()
        {
            CancelCalls++;
        }
    }

    private sealed class TestOutputOperationLeaseProvider : IOutputOperationLeaseProvider
    {
        public int AcquireCalls { get; private set; }

        public IDisposable TryBeginOutputOperation()
        {
            AcquireCalls++;
            return StandaloneOutputOperationLeaseProvider.Instance.TryBeginOutputOperation();
        }
    }

    private sealed class TestEditorContext : IEditorContext
    {
        public IEditorContextCloseService CloseService { get; } = new TestCloseService();

        public ValueTask DisposeAsync()
        {
            IsEnabled.Dispose();
            return ValueTask.CompletedTask;
        }

        public CoreObject Object { get; } = new TestCoreObject();

        public EditorExtension Extension => null!;

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

        public IKnownEditorCommands? Commands => null;

        public T? FindToolTab<T>(Func<T, bool> condition)
            where T : IToolContext
            => default;

        public T? FindToolTab<T>()
            where T : IToolContext
            => default;

        public ValueTask<bool> OpenToolTabAsync(IToolContext item) => ValueTask.FromResult(false);

        public ValueTask CloseToolTabAsync(IToolContext item) => ValueTask.CompletedTask;

        public object? GetService(Type serviceType) => null;
    }

    private sealed class TestCoreObject : CoreObject;

    private sealed class TestCloseService : IEditorContextCloseService
    {
        public EditorContextHostToken HostToken { get; } = new();

        public EditorContextCloseRequest RequestClose(IEditorContext context) => default;
    }
}
