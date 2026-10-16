using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Models;
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
    [AvaloniaTest]
    public async Task Asynchronous_menu_command_returns_its_complete_operation()
    {
        await TestReset.ResetShellAsync();
        await TestShell.CreateProjectAsync("context-command-dispatch");

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = TestShell.MainViewModel.MenuBar.SaveAll
            .Subscribe(() => gate.Task);
        var execution = new ContextCommandExecution("SaveAll");

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

        var execution = new ContextCommandExecution("SaveAll");

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

            Task operation = SceneEditorExtension.Instance.RunAsync(editor, new ContextCommandExecution("PlayPause"));

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
    [TestCase("Cut")]
    [TestCase("Copy")]
    [TestCase("Paste")]
    public async Task Timeline_handler_returns_clipboard_command_operations(string commandName)
    {
        await TestReset.ResetShellAsync();
        try
        {
            EditViewModel editor = await TestShell.OpenNewSceneEditorAsync(
                $"context-command-timeline-{commandName.ToLowerInvariant()}");
            var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
            ElementAddResult added = await adder.AddAsync([new ElementDescription(
                Start: TimeSpan.Zero,
                Length: TimeSpan.FromSeconds(1),
                Layer: 0,
                Source: new ElementSource.EngineObject(() => new RectShape()))],
                CancellationToken.None);
            Assert.That(added.IsSuccess, Is.True);
            HeadlessTestHelpers.Settle();
            TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
            ElementViewModel target = timeline.Elements.Single();
            timeline.SelectElement(target);
            AsyncReactiveCommand command = commandName switch
            {
                "Cut" => target.Cut,
                "Copy" => target.Copy,
                _ => timeline.Paste,
            };
            var gate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = command.Subscribe(() => gate.Task);

            Task operation = TimelineTabExtension.Instance.RunAsync(timeline, new ContextCommandExecution(commandName));

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
            Assert.That(handler.LastExecution?.CommandName, Is.EqualTo("AwaitableCommand"));
        });

        gate.SetResult();
        await operation;

        Assert.That(operation.IsCompletedSuccessfully, Is.True);
    }

    private sealed class AwaitableViewExtension : ViewExtension
    {
        public override IEnumerable<ContextCommandDefinition> ContextCommands =>
        [
            new ContextCommandDefinition("AwaitableCommand", "Awaitable Command") { Id = "test.awaitableCommand" },
        ];
    }

    private sealed class AwaitableHandler(Task operation) : IContextCommandHandler
    {
        public ContextCommandExecution? LastExecution { get; private set; }

        public Task ExecuteAsync(ContextCommandExecution execution)
        {
            LastExecution = execution;
            return operation;
        }
    }
}
