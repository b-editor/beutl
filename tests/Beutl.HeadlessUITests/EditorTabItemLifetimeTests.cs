using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Services;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class EditorTabItemLifetimeTests
{
    [Test]
    public async Task DisposeAsync_PublishesOneSharedTaskBeforeContextTeardown()
    {
        var context = new BlockingEditorContext();
        var tab = new EditorTabItem(context);

        Task first = tab.DisposeAsync().AsTask();
        Task second = tab.DisposeAsync().AsTask();

        Assert.That(second, Is.SameAs(first));
        Assert.That(context.DisposeStarted.Task.IsCompleted, Is.True);

        context.ReleaseDispose();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(context.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ReplaceContextAsync_RejectsAndDisposesNewContextWhenCloseWins()
    {
        var oldContext = new BlockingEditorContext();
        var newContext = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(oldContext);

        Task close = tab.DisposeAsync().AsTask();
        Task<bool> replacement = tab.ReplaceContextAsync(newContext).AsTask();

        oldContext.ReleaseDispose();
        Assert.That(await replacement.WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
        await newContext.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await close.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(newContext.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ReplacementContextDisposalCanReenterTabCloseWithoutDeadlock()
    {
        var oldContext = new BlockingEditorContext(blockDispose: false);
        var newContext = new BlockingEditorContext(blockDispose: false);
        var tab = new EditorTabItem(oldContext);
        oldContext.OnDispose = () => tab.DisposeAsync();

        bool replaced = await tab.ReplaceContextAsync(newContext)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await tab.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(replaced, Is.False);
            Assert.That(oldContext.DisposeCount, Is.EqualTo(1));
            Assert.That(newContext.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ClosingOneTabWaitsForSiblingCloseStartedByItsContext()
    {
        var service = new EditorService(new ExtensionProvider());
        var firstContext = new BlockingEditorContext(blockDispose: false);
        var secondContext = new BlockingEditorContext(blockDispose: false);
        var first = new EditorTabItem(firstContext);
        var second = new EditorTabItem(secondContext);
        service.AddTabItem(first);
        service.AddTabItem(second);
        firstContext.OnDispose = () => service.CloseTabItem(second);

        await service.CloseTabItem(first).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(firstContext.DisposeCount, Is.EqualTo(1));
            Assert.That(secondContext.DisposeCount, Is.EqualTo(1));
            Assert.That(service.TabItems, Is.Empty);
        });
    }

    [Test]
    public async Task MutualSiblingCloseUsesAncestorIdentityWithoutCycle()
    {
        var service = new EditorService(new ExtensionProvider());
        var firstContext = new BlockingEditorContext(blockDispose: false);
        var secondContext = new BlockingEditorContext(blockDispose: false);
        var first = new EditorTabItem(firstContext);
        var second = new EditorTabItem(secondContext);
        service.AddTabItem(first);
        service.AddTabItem(second);
        firstContext.OnDispose = () => service.CloseTabItem(second);
        secondContext.OnDispose = () => service.CloseTabItem(first);

        await service.CloseTabItem(first).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await second.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(firstContext.DisposeCount, Is.EqualTo(1));
            Assert.That(secondContext.DisposeCount, Is.EqualTo(1));
            Assert.That(service.TabItems, Is.Empty);
        });
    }

    private sealed class BlockingEditorContext : IEditorContext
    {
        private readonly bool _blockDispose;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingEditorContext(bool blockDispose = true)
        {
            _blockDispose = blockDispose;
        }

        public TaskCompletionSource DisposeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount { get; private set; }

        public Func<ValueTask>? OnDispose { get; set; }

        public CoreObject Object { get; } = new Scene(16, 16, string.Empty);

        public EditorExtension Extension { get; } = TestEditorExtension.Instance;

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

        public IKnownEditorCommands? Commands => null;

        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            DisposeStarted.TrySetResult();
            if (OnDispose is { } onDispose)
                await onDispose();
            if (_blockDispose)
                await _release.Task.ConfigureAwait(false);

            Disposed.TrySetResult();
        }

        public void ReleaseDispose() => _release.TrySetResult();

        public T? FindToolTab<T>(Func<T, bool> condition) where T : IToolContext => default;

        public T? FindToolTab<T>() where T : IToolContext => default;

        public ValueTask<bool> OpenToolTabAsync(IToolContext item) => new(false);

        public ValueTask CloseToolTabAsync(IToolContext item)
        {
            return ValueTask.CompletedTask;
        }

        public object? GetService(Type serviceType) => null;
    }

    private sealed class TestEditorExtension : EditorExtension
    {
        public static readonly TestEditorExtension Instance = new();

        public override FilePickerFileType GetFilePickerFileType() => new("Test");

        public override IconSource? GetIcon() => null;

        public override bool TryCreateEditor(CoreObject obj, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Control? editor)
        {
            editor = null;
            return false;
        }

        public override bool MatchFileExtension(string ext) => false;

        public override bool TryCreateContext(
            CoreObject obj,
            IEditorContextServices services,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEditorContext? context)
        {
            context = null;
            return false;
        }
    }
}
