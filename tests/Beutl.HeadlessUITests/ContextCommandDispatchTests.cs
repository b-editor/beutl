using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Reactive.Bindings;
using static Beutl.HeadlessUITests.CommandPaletteTestHost;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ContextCommandDispatchTests
{
    // The commands that the app and its built-in extensions register all run from their definitions.
    [AvaloniaTest]
    public void Every_registered_command_has_a_handler()
    {
        ContextCommandDefinition[] definitions = [.. TestShell.MainViewModel.ContextCommandManager.GetDefinitions()
            .Select(entry => entry.Definition)];

        Assert.That(definitions.Where(definition => definition.HasHandler).Select(definition => definition.Id),
            Is.EquivalentTo(definitions.Select(definition => definition.Id)));
    }

    [AvaloniaTest]
    public async Task Asynchronous_menu_command_returns_its_complete_operation()
    {
        await TestReset.ResetShellAsync();
        await TestShell.CreateProjectAsync("context-command-dispatch");

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = TestShell.MainViewModel.MenuBar.SaveAll
            .Subscribe(() => gate.Task);
        var execution = new ContextCommandExecution("main.saveAll");

        Task operation = MainViewExtension.Instance.RunAsync(TestShell.MainViewModel, execution);

        Assert.That(operation.IsCompleted, Is.False);

        gate.SetResult();
        await operation;

        Assert.That(operation.IsCompletedSuccessfully, Is.True);
        await TestReset.ResetShellAsync();
    }

    [AvaloniaTest]
    public async Task Disabled_menu_command_returns_a_completed_operation()
    {
        await TestReset.ResetShellAsync();

        var execution = new ContextCommandExecution("main.saveAll");

        // No project is open, so the command is disabled and nothing may be dispatched.
        Task operation = MainViewExtension.Instance.RunAsync(TestShell.MainViewModel, execution);

        Assert.That(operation.IsCompletedSuccessfully, Is.True);
    }

    [AvaloniaTest]
    public async Task Menu_command_adapter_returns_any_async_reactive_command_operation()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncReactiveCommand();
        using IDisposable subscription = command.Subscribe(() => gate.Task);

        Task operation = MenuBarViewModel.ExecuteCommandAsync(command);

        Assert.That(operation.IsCompleted, Is.False);
        gate.SetResult();
        await operation;
        Assert.That(operation.IsCompletedSuccessfully, Is.True);
    }

    [AvaloniaTest]
    public async Task Edit_handler_returns_the_play_pause_operation()
    {
        await TestReset.ResetShellAsync();
        try
        {
            EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("context-command-edit-handler");
            var gate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = editor.Player.PlayPause.Subscribe(() => gate.Task);

            Task operation = SceneEditorExtension.Instance.RunAsync(editor, new ContextCommandExecution("editor.playPause"));

            Assert.That(operation.IsCompleted, Is.False);
            gate.SetResult();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(operation.IsCompletedSuccessfully, Is.True);
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    [TestCase("timeline.cut")]
    [TestCase("timeline.copy")]
    [TestCase("timeline.paste")]
    public async Task Timeline_handler_returns_clipboard_command_operations(string commandId)
    {
        await TestReset.ResetShellAsync();
        try
        {
            EditViewModel editor = await TestShell.OpenNewSceneEditorAsync(
                $"context-command-{commandId.ToLowerInvariant()}");
            var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
            ElementAddResult added = await adder.AddAsync([new ElementDescription(
                Start: MediaTime.Zero,
                Length: MediaTime.FromSeconds(1),
                Layer: 0,
                Source: new ElementSource.EngineObject(() => new RectShape()))],
                CancellationToken.None);
            Assert.That(added.IsSuccess, Is.True);
            HeadlessTestHelpers.Settle();
            TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
            ElementViewModel target = timeline.Elements.Single();
            timeline.SelectElement(target);
            AsyncReactiveCommand command = commandId switch
            {
                "timeline.cut" => target.Cut,
                "timeline.copy" => target.Copy,
                _ => timeline.Paste,
            };
            var gate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = command.Subscribe(() => gate.Task);

            Task operation = TimelineTabExtension.Instance.RunAsync(timeline, new ContextCommandExecution(commandId));

            Assert.That(operation.IsCompleted, Is.False);
            gate.SetResult();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(operation.IsCompletedSuccessfully, Is.True);
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Command_palette_returns_the_handler_operation()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new AwaitableHandler(gate.Task);
        var extension = new AwaitableViewExtension();
        var manager = new ContextCommandManager(new ContextCommandSettingsStore());
        manager.Register(extension);
        var extensionProvider = new ExtensionProvider();
        var editorService = new EditorService(extensionProvider);
        var service = new CommandPaletteService(
            manager,
            new FixedHandlerProvider(handler),
            menuBarAccessor: () => null,
            editorService,
            extensionProvider);
        using var viewModel = new CommandPaletteViewModel(service, editorService);
        viewModel.Open();
        viewModel.SelectedCommand.Value = viewModel.FilteredCommands.Single();

        Task operation = viewModel.ExecuteSelectedAsync();

        Assert.Multiple(() =>
        {
            Assert.That(operation.IsCompleted, Is.False);
            Assert.That(viewModel.IsOpen.Value, Is.False);
            Assert.That(handler.LastExecution?.CommandId, Is.EqualTo("test.awaitableCommand"));
        });

        gate.SetResult();
        await operation;

        Assert.That(operation.IsCompletedSuccessfully, Is.True);
    }

    private sealed class AwaitableViewExtension : ViewExtension
    {
        public override IEnumerable<ContextCommandDefinition> ContextCommands =>
        [
            new ContextCommandDefinition<AwaitableHandler>("test.awaitableCommand", "Awaitable Command")
            {
                ExecuteOnAsync = (handler, execution) => handler.ExecuteAsync(execution)
            },
        ];
    }

    private sealed class AwaitableHandler(Task operation)
    {
        public ContextCommandExecution? LastExecution { get; private set; }

        public Task ExecuteAsync(ContextCommandExecution execution)
        {
            LastExecution = execution;
            return operation;
        }
    }
}
