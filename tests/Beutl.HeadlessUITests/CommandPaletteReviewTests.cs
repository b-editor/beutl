using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class CommandPaletteReviewTests
{
    private static (CommandPaletteViewModel Palette, EditorService Editor) CreatePalette(Func<ContextCommandExecution, Task> execute)
    {
        var provider = new ExtensionProvider();
        var editor = new EditorService(provider);
        var manager = new ContextCommandManager(new ContextCommandSettingsStore(), new ContextCommandHandlerRegistry());
        manager.Register(new PromptExtension());
        return (new CommandPaletteViewModel(new CommandPaletteService(manager, new Handler(execute), () => null, editor, provider), editor), editor);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Switching_editors_cancels_delayed_prompts_before_or_between_steps(bool betweenSteps)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool waiting = false;
        string? result = "pending";
        var (palette, editor) = CreatePalette(async execution =>
        {
            if (betweenSteps)
                await execution.Interaction!.ShowInputAsync(new ContextCommandInputOptions());
            waiting = true;
            await gate.Task;
            result = await execution.Interaction!.ShowInputAsync(new ContextCommandInputOptions());
        });
        using (palette)
        {
            palette.Open();
            Task operation = palette.ExecuteSelectedAsync();
            if (betweenSteps) await palette.ExecuteSelectedAsync();
            await UntilAsync(() => waiting);
            var context = new Mock<IEditorContext>();
            context.SetupGet(item => item.Object).Returns(new Scene());
            context.SetupGet(item => item.Extension).Returns(SceneEditorExtension.Instance);
            editor.SelectedTabItem.Value = new EditorTabItem(context.Object);
            HeadlessTestHelpers.Settle();
            gate.SetResult();
            await UntilAsync(() => operation.IsCompleted || palette.Prompt.Value is { IsCompleted: false });
            bool reopened = palette.IsOpen.Value;
            palette.Close();
            await operation;
            Assert.Multiple(() =>
            {
                Assert.That(reopened, Is.False);
                Assert.That(result, Is.Null);
            });
        }
    }

    [AvaloniaTest]
    [TestCase("query")]
    [TestCase("submit")]
    [TestCase("initial")]
    public async Task Validator_failures_abort_the_interaction_and_report_an_error(string phase)
    {
        var notifications = new CapturedNotifications();
        INotificationServiceHandler previous = NotificationService.Handler;
        NotificationService.Handler = notifications;
        bool fail = phase == "initial";
        string? result = "pending";
        var (palette, _) = CreatePalette(async execution =>
            result = await execution.Interaction!.ShowInputAsync(new ContextCommandInputOptions
            {
                Validate = _ => fail ? throw new InvalidOperationException("Validator failed.") : null
            }));
        using (palette)
        {
            try
            {
                palette.Open();
                Task operation = palette.ExecuteSelectedAsync();
                fail = true;
                Exception? failure = null;
                try
                {
                    if (phase == "submit") await palette.ExecuteSelectedAsync();
                    else if (phase == "query") palette.Query.Value = "edited value";
                }
                catch (Exception ex) { failure = ex; }
                bool closed = !palette.IsOpen.Value && palette.Prompt.Value is null;
                palette.Close();
                await operation;
                Assert.Multiple(() =>
                {
                    Assert.That(failure, Is.Null);
                    Assert.That(closed, Is.True);
                    Assert.That(result, Is.Null);
                    Assert.That(notifications.Items, Has.Count.EqualTo(1));
                });
            }
            finally
            {
                palette.Close();
                NotificationService.Handler = previous;
            }
        }
    }

    [AvaloniaTest]
    public async Task Cancellation_callback_failures_cannot_skip_prompt_cleanup()
    {
        var notifications = new CapturedNotifications();
        INotificationServiceHandler previous = NotificationService.Handler;
        NotificationService.Handler = notifications;
        string? result = "pending";
        var (palette, _) = CreatePalette(async execution =>
        {
            using var registration = execution.CancellationToken.Register(() => throw new InvalidOperationException("Cancellation callback failed."));
            result = await execution.Interaction!.ShowInputAsync(new ContextCommandInputOptions());
        });
        using (palette)
        {
            try
            {
                palette.Open();
                Task operation = palette.ExecuteSelectedAsync();
                Exception? failure = null;
                try { palette.Close(); }
                catch (Exception ex) { failure = ex; }
                bool closed = !palette.IsOpen.Value && palette.Prompt.Value is null;
                palette.Close();
                await operation;
                Assert.Multiple(() =>
                {
                    Assert.That(failure, Is.Null);
                    Assert.That(closed, Is.True);
                    Assert.That(result, Is.Null);
                    Assert.That(notifications.Items, Has.Count.EqualTo(1));
                });
            }
            finally
            {
                palette.Close();
                NotificationService.Handler = previous;
            }
        }
    }

    [AvaloniaTest]
    public async Task Retired_non_notifier_extension_can_unload_while_its_snapshot_is_retained()
    {
        var (service, snapshot, assembly) = CreateRetiredCollectibleSnapshot();
        for (int i = 0; i < 20 && assembly.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(10);
        }
        Assert.That(assembly.IsAlive, Is.False);
        Assert.That(snapshot.Single().CanExecute(), Is.False);
        GC.KeepAlive(snapshot);
        GC.KeepAlive(service);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (CommandPaletteService Service, IReadOnlyList<PaletteCommand> Snapshot, WeakReference Assembly) CreateRetiredCollectibleSnapshot()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("PaletteCollectible" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        Type type = assembly.DefineDynamicModule("Palette").DefineType("CollectibleCommand", TypeAttributes.Public, typeof(CollectibleCommandExtensionBase)).CreateType()!;
        var extension = (ViewExtension)Activator.CreateInstance(type)!;
        var provider = new ExtensionProvider();
        provider.AddExtensions(-43004, [extension]);
        var manager = new ContextCommandManager(new ContextCommandSettingsStore(), new ContextCommandHandlerRegistry());
        manager.Register(extension);
        var service = new CommandPaletteService(manager, new Handler(_ => Task.CompletedTask), () => null, new EditorService(provider), provider);
        IReadOnlyList<PaletteCommand> snapshot = service.EnumerateCommands();
        manager.Unregister(extension);
        provider.RemoveExtensions(-43004).DrainAsync().AsTask().GetAwaiter().GetResult();
        return (service, snapshot, new WeakReference(type.Assembly));
    }

    public class CollectibleCommandExtensionBase : ViewExtension, IContextCommandHandler
    {
        public override IEnumerable<ContextCommandDefinition> ContextCommands =>
        [
            new("Collectible", "Collectible command") { Scope = ContextCommandScope.Extension }
        ];
        public Task ExecuteAsync(ContextCommandExecution execution) => Task.CompletedTask;
    }

    private sealed class PromptExtension : ViewExtension
    {
        public override IEnumerable<ContextCommandDefinition> ContextCommands => [new("Prompt", "Prompt")];
    }

    private sealed class Handler(Func<ContextCommandExecution, Task> execute) : ICommandPaletteHandlerProvider, IContextCommandHandler
    {
        public IContextCommandHandler? Resolve(Type extensionType) => this;
        public Task ExecuteAsync(ContextCommandExecution execution) => execute(execution);
    }

    private sealed class CapturedNotifications : INotificationServiceHandler
    {
        public List<Notification> Items { get; } = [];
        public void Show(Notification notification) => Items.Add(notification);
    }

    private static async Task UntilAsync(Func<bool> predicate)
    {
        for (int i = 0; i < 100; i++)
        {
            HeadlessTestHelpers.Render();
            if (predicate()) return;
            await Task.Delay(10);
        }
        Assert.Fail("The palette did not reach the expected state.");
    }
}
