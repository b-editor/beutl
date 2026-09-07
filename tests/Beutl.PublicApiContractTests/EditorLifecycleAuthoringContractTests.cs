using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Extensibility;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class EditorLifecycleAuthoringContractTests
{
    [Test]
    public async Task ExternalHost_CanOwnAndCloseAsyncEditorContexts()
    {
        var hostToken = new EditorContextHostToken();
        var closeService = new ExternalCloseService(hostToken);
        var services = new ExternalEditorContextServices(closeService);
        var context = new ExternalEditorContext(services.CloseService);
        var tool = new ExternalToolContext();
        var extension = new ExternalEditorExtension();

        IEditorContext? authoredContext = await extension.CreateContextAsync(null!, services);
        Assert.That(authoredContext, Is.Not.Null);
        await authoredContext!.DisposeAsync();

        Assert.That(services.TryGetService<IEditorContextCloseService>(out var resolved), Is.True);
        Assert.That(resolved, Is.SameAs(closeService));
        Assert.That(hostToken.TryAcquireContext(context, out var lease), Is.True);
        Assert.That(hostToken.TryAcquireContext(context, out var duplicateLease), Is.False);
        Assert.That(duplicateLease, Is.Null);
        Assert.That(new EditorContextHostToken().TryAcquireContext(context, out _), Is.False);

        Assert.That(await context.OpenToolTabAsync(tool), Is.True);
        Assert.That(context.FindToolTab<ExternalToolContext>(), Is.SameAs(tool));

        EditorContextCloseRequest request = context.CloseService.RequestClose(context);
        Assert.That(request.Status, Is.EqualTo(EditorContextCloseRequestStatus.Accepted));
        await request.Completion;

        await context.CloseToolTabAsync(tool);
        Assert.That(tool.DisposeCount, Is.EqualTo(1));

        var claimedTool = new ExternalToolContext();
        var firstToolHost = new ToolContextHostToken();
        var secondToolHost = new ToolContextHostToken();
        Assert.That(firstToolHost.TryAcquireContext(claimedTool, out var toolLease), Is.True);
        Assert.That(secondToolHost.TryAcquireContext(claimedTool, out var competingToolLease), Is.False);
        Assert.That(competingToolLease, Is.Null);
        await claimedTool.DisposeAsync();
        toolLease!.Dispose();

        await context.DisposeAsync();
        lease!.Dispose();
        Assert.That(context.DisposeCount, Is.EqualTo(1));
    }

    private sealed class ExternalEditorContextServices(IEditorContextCloseService closeService)
        : IEditorContextServices
    {
        public IExtensionProvider ExtensionProvider => null!;

        public IEditorContextCloseService CloseService { get; } = closeService;

        public bool TryGetService<T>([NotNullWhen(true)] out T? service)
            where T : class
        {
            service = CloseService as T;
            return service is not null;
        }
    }

    private sealed class ExternalEditorExtension : EditorExtension
    {
        public override FilePickerFileType GetFilePickerFileType() => new("External");

        public override IconSource? GetIcon() => null;

        public override bool TryCreateEditor(
            CoreObject obj,
            [NotNullWhen(true)] out Control? editor)
        {
            editor = null;
            return false;
        }

        public override ValueTask<IEditorContext?> CreateContextAsync(
            CoreObject obj,
            IEditorContextServices services)
            => ValueTask.FromResult<IEditorContext?>(new ExternalEditorContext(services.CloseService));

        public override bool MatchFileExtension(string ext) => false;
    }

    private sealed class ExternalCloseService(EditorContextHostToken hostToken)
        : IEditorContextCloseService
    {
        public EditorContextHostToken HostToken { get; } = hostToken;

        public EditorContextCloseRequest RequestClose(IEditorContext context)
            => new(EditorContextCloseRequestStatus.Accepted, Task.CompletedTask);
    }

    private sealed class ExternalEditorContext(IEditorContextCloseService closeService)
        : IEditorContext
    {
        private IToolContext? _tool;

        public int DisposeCount { get; private set; }

        public IEditorContextCloseService CloseService { get; } = closeService;

        public CoreObject Object => null!;

        public EditorExtension Extension => null!;

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactiveProperty<bool>(true);

        public IKnownEditorCommands? Commands => null;

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        public T? FindToolTab<T>(Func<T, bool> condition)
            where T : IToolContext
            => _tool is T typed && condition(typed) ? typed : default;

        public T? FindToolTab<T>()
            where T : IToolContext
            => _tool is T typed ? typed : default;

        public ValueTask<bool> OpenToolTabAsync(IToolContext item)
        {
            _tool = item;
            return ValueTask.FromResult(true);
        }

        public async ValueTask CloseToolTabAsync(IToolContext item)
        {
            if (ReferenceEquals(_tool, item))
            {
                _tool = null;
                await item.DisposeAsync();
            }
        }

        public object? GetService(Type serviceType) => null;
    }

    private sealed class ExternalToolContext : IToolContext
    {
        public int DisposeCount { get; private set; }

        public ToolTabExtension Extension => null!;

        public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

        public IReadOnlyReactiveProperty<string> Header { get; } = new ReactiveProperty<string>("External");

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        public void ReadFromJson(JsonObject json)
        {
        }

        public void WriteToJson(JsonObject json)
        {
        }

        public object? GetService(Type serviceType) => null;
    }
}
