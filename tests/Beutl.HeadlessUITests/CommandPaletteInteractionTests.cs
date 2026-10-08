using System.Diagnostics.CodeAnalysis;
using System.Reactive;
using System.Reactive.Subjects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class CommandPaletteInteractionTests
{
    private static (CommandPaletteViewModel Palette, EditorService Editor) CreatePalette(
        Func<ContextCommandExecution, Task> execute)
    {
        var provider = new ExtensionProvider();
        var editor = new EditorService(provider);
        var manager = new ContextCommandManager(new ContextCommandSettingsStore(), new ContextCommandHandlerRegistry());
        manager.Register(new PromptExtension());
        var service = new CommandPaletteService(
            manager, new FixedHandlerProvider(new Handler(execute)), () => null, editor, provider);
        return (new CommandPaletteViewModel(service, editor), editor);
    }

    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        HeadlessTestHelpers.Settle();
    }

    private static async Task UntilAsync(Func<bool> predicate)
    {
        for (int i = 0; i < 100; i++)
        {
            HeadlessTestHelpers.Render();
            if (predicate()) return;
            await Task.Delay(10);
        }

        Assert.Fail("The command palette did not reach the expected step.");
    }

    private static void Capture(Window window, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("BEUTL_COMMAND_PALETTE_CAPTURE_DIR");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        HeadlessTestHelpers.Render();
        using var frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null);
        frame!.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    [AvaloniaTest]
    [TestCase(320, false)]
    [TestCase(900, false)]
    [TestCase(320, true)]
    [TestCase(900, true)]
    public async Task Input_then_filtered_pick_works_through_the_real_view(int width, bool light)
    {
        string? name = null;
        int? choice = null;
        int invocations = 0;
        var (palette, _) = CreatePalette(async execution =>
        {
            invocations++;
            name = await execution.Interaction!.ShowInputAsync(new ContextCommandInputOptions
            {
                Title = "Save a workspace layout",
                Prompt = "Choose a name to use for this layout.",
                Value = "Editing",
                Validate = value => string.IsNullOrWhiteSpace(value) ? "Enter a layout name." : null
            });
            if (name is null) return;
            var selected = await execution.Interaction.ShowQuickPickAsync<int>(
            [
                new("Editing", 1, "Timeline and media library"),
                new("Color grading", 2, "Scopes and color controls"),
                new("Audio mixing", 3, "Mixer and audio meters")
            ], new ContextCommandPickOptions { Title = "Choose a workspace layout" });
            choice = selected?.Value;
        });
        using (palette)
        {
            var view = new CommandPaletteView { DataContext = palette };
            var window = new Window
            {
                Content = view,
                Width = width,
                Height = 640,
                RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
            };
            window.Show();
            try
            {
                palette.Open();
                HeadlessTestHelpers.Render();
                Task operation = palette.ExecuteSelectedAsync();
                HeadlessTestHelpers.Render();
                TextBox input = view.FindControl<TextBox>("QueryTextBox")!;
                Assert.That(input.IsFocused, Is.True);
                Assert.That(input.SelectedText, Is.EqualTo("Editing"));

                window.KeyTextInput(" ");
                Press(window, Key.Enter);
                Assert.Multiple(() =>
                {
                    Assert.That(palette.Prompt.Value!.ValidationError.Value, Is.EqualTo("Enter a layout name."));
                    Assert.That(operation.IsCompleted, Is.False);
                    Assert.That(view.FindControl<Button>("ConfirmButton")!.IsEnabled, Is.False);
                });
                Capture(window, $"invalid-{width}-{light}");

                input.SelectAll();
                window.KeyTextInput("My layout");
                Press(window, Key.Home);
                Assert.That(input.CaretIndex, Is.Zero, "Home edits text during an input step.");
                Capture(window, $"input-{width}-{light}");
                Press(window, Key.Enter);
                await UntilAsync(() => palette.Prompt.Value is { IsPick: true });

                Assert.That(input.IsFocused, Is.True);
                Assert.That(palette.Query.Value, Is.Empty);
                Capture(window, $"pick-{width}-{light}");
                Press(window, Key.Down);
                Assert.That(palette.Prompt.Value!.SelectedChoice.Value!.Label, Is.EqualTo("Color grading"));
                Press(window, Key.Home);
                Assert.That(palette.Prompt.Value.SelectedChoice.Value!.Label, Is.EqualTo("Editing"));
                window.KeyTextInput("absent");
                Assert.That(palette.Prompt.Value!.HasNoResults.Value, Is.True);
                Press(window, Key.Enter);
                Assert.That(operation.IsCompleted, Is.False, "An empty result cannot be confirmed.");

                input.SelectAll();
                window.KeyTextInput("controls");
                Assert.That(palette.Prompt.Value.FilteredChoices.Single().Label, Is.EqualTo("Color grading"));
                Press(window, Key.Enter);
                Press(window, Key.Enter);
                await operation;
                Assert.Multiple(() =>
                {
                    Assert.That(name, Is.EqualTo("My layout"));
                    Assert.That(choice, Is.EqualTo(2));
                    Assert.That(invocations, Is.EqualTo(1));
                    Assert.That(palette.IsOpen.Value, Is.False);
                    Assert.That(palette.Prompt.Value, Is.Null);
                });
            }
            finally
            {
                palette.Close();
                window.Close();
                HeadlessTestHelpers.Settle();
            }
        }
    }

    [AvaloniaTest]
    public async Task Escape_cancels_the_step_and_prevents_later_prompts()
    {
        string? input = "pending";
        string? later = "pending";
        CancellationToken token = default;
        var (palette, _) = CreatePalette(async execution =>
        {
            token = execution.CancellationToken;
            input = await execution.Interaction!.ShowInputAsync(new ContextCommandInputOptions());
            later = await execution.Interaction.ShowInputAsync(new ContextCommandInputOptions());
        });
        using (palette)
        {
            var previousFocus = new TextBox();
            var window = new Window
            {
                Content = new Panel { Children = { previousFocus, new CommandPaletteView { DataContext = palette } } },
                Width = 640,
                Height = 480
            };
            window.Show();
            try
            {
                previousFocus.Focus();
                palette.Open();
                Task operation = palette.ExecuteSelectedAsync();
                HeadlessTestHelpers.Render();
                Press(window, Key.Escape);
                await operation;
                HeadlessTestHelpers.Settle();
                Assert.Multiple(() =>
                {
                    Assert.That(input, Is.Null);
                    Assert.That(later, Is.Null);
                    Assert.That(token.IsCancellationRequested, Is.True);
                    Assert.That(palette.IsOpen.Value, Is.False);
                    Assert.That(previousFocus.IsFocused, Is.True);
                });
            }
            finally
            {
                window.Close();
                HeadlessTestHelpers.Settle();
            }
        }
    }

    [AvaloniaTest]
    public async Task A_prompt_requested_after_an_await_reopens_for_the_same_invocation()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? result = null;
        var (palette, _) = CreatePalette(async execution =>
        {
            await gate.Task;
            result = await execution.Interaction!.ShowInputAsync(new ContextCommandInputOptions());
        });
        using (palette)
        {
            palette.Open();
            Task operation = palette.ExecuteSelectedAsync();
            Assert.That(palette.IsOpen.Value, Is.False);
            gate.SetResult();
            await UntilAsync(() => palette.Prompt.Value is not null);
            Assert.That(palette.IsOpen.Value, Is.True);
            palette.Query.Value = string.Empty;
            await palette.ExecuteSelectedAsync();
            await operation;
            Assert.That(result, Is.EqualTo(string.Empty), "Empty text and cancellation are distinct results.");
        }
    }

    [AvaloniaTest]
    public async Task Old_invocations_cannot_reopen_or_close_a_new_prompt()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int invocation = 0;
        string? oldResult = "pending";
        var (palette, _) = CreatePalette(async execution =>
        {
            if (++invocation == 1)
            {
                await gate.Task;
                oldResult = await execution.Interaction!.ShowInputAsync(new ContextCommandInputOptions());
            }
            else
            {
                await execution.Interaction!.ShowInputAsync(new ContextCommandInputOptions { Title = "New command" });
            }
        });
        using (palette)
        {
            palette.Open();
            Task oldOperation = palette.ExecuteSelectedAsync();
            palette.Open();
            Task newOperation = palette.ExecuteSelectedAsync();
            var newPrompt = palette.Prompt.Value;
            gate.SetResult();
            await oldOperation;
            Assert.Multiple(() =>
            {
                Assert.That(oldResult, Is.Null);
                Assert.That(palette.IsOpen.Value, Is.True);
                Assert.That(palette.Prompt.Value, Is.SameAs(newPrompt));
            });
            palette.Close();
            await newOperation;
        }
    }

    [AvaloniaTest]
    public async Task Cancellation_token_and_disposal_release_pending_requests()
    {
        using var cancellation = new CancellationTokenSource();
        string? result = "pending";
        var (palette, _) = CreatePalette(async execution =>
            result = await execution.Interaction!.ShowInputAsync(new ContextCommandInputOptions(), cancellation.Token));
        palette.Open();
        Task operation = palette.ExecuteSelectedAsync();
        cancellation.Cancel();
        await UntilAsync(() => !palette.IsOpen.Value);
        await operation;
        Assert.That(result, Is.Null);
        palette.Dispose();

        var (disposedPalette, _) = CreatePalette(async execution =>
            result = await execution.Interaction!.ShowInputAsync(new ContextCommandInputOptions()));
        disposedPalette.Open();
        Task disposedOperation = disposedPalette.ExecuteSelectedAsync();
        Assert.That(disposedOperation.IsCompleted, Is.False);
        disposedPalette.Dispose();
        await disposedOperation;
        Assert.That(result, Is.Null);
    }

    [AvaloniaTest]
    public async Task Switching_editor_tabs_cancels_a_pending_prompt()
    {
        string? result = "pending";
        var (palette, editor) = CreatePalette(async execution =>
            result = await execution.Interaction!.ShowInputAsync(new ContextCommandInputOptions()));
        using (palette)
        {
            palette.Open();
            Task operation = palette.ExecuteSelectedAsync();
            var context = new Mock<IEditorContext>();
            context.SetupGet(item => item.Object).Returns(new Scene());
            context.SetupGet(item => item.Extension).Returns(SceneEditorExtension.Instance);
            editor.SelectedTabItem.Value = new EditorTabItem(context.Object);
            HeadlessTestHelpers.Settle();
            await operation;
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Null);
                Assert.That(palette.IsOpen.Value, Is.False);
            });
        }
    }

    [AvaloniaTest]
    public async Task Failure_after_confirmation_cleans_up_the_interaction()
    {
        var (palette, _) = CreatePalette(async execution =>
        {
            await execution.Interaction!.ShowInputAsync(new ContextCommandInputOptions());
            throw new InvalidOperationException("Command failed.");
        });
        using (palette)
        {
            palette.Open();
            Task operation = palette.ExecuteSelectedAsync();
            await palette.ExecuteSelectedAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await operation);
            Assert.That(palette.IsOpen.Value, Is.False);
            palette.Open();
            Assert.That(palette.FilteredCommands, Has.Count.EqualTo(1));
        }
    }

    [AvaloniaTest]
    public async Task Closed_tool_commands_use_the_extension_without_constructing_a_tool_context()
    {
        var provider = new ExtensionProvider();
        var editor = new EditorService(provider);
        var extension = new ClosedToolExtension();
        provider.AddExtensions(-43001, [extension]);
        var manager = new ContextCommandManager(new ContextCommandSettingsStore(), new ContextCommandHandlerRegistry());
        manager.Register(extension);
        var service = new CommandPaletteService(manager, new FixedHandlerProvider(null), () => null, editor, provider);
        using var palette = new CommandPaletteViewModel(service, editor);
        palette.Open();
        Assert.That(palette.FilteredCommands.Select(item => item.DisplayName), Is.EqualTo(new[] { "Closed tool command" }));
        await palette.ExecuteSelectedAsync();
        Assert.Multiple(() =>
        {
            Assert.That(extension.Execution?.CommandName, Is.EqualTo("Global"));
            Assert.That(extension.Execution?.EditorContext, Is.Null);
            Assert.That(extension.Execution?.Services?.ExtensionProvider, Is.SameAs(provider));
            Assert.That(extension.ContextCreations, Is.Zero);
        });

        PaletteCommand command = service.EnumerateCommands().Single();
        extension.Enabled = false;
        await command.ExecuteAsync();
        Assert.That(extension.Executions, Is.EqualTo(1), "CanExecute is checked again immediately before execution.");
        provider.RemoveExtensions(-43001);
        Assert.That(command.CanExecute(), Is.False);
        await command.ExecuteAsync();
        Assert.That(extension.Executions, Is.EqualTo(1), "Removed extensions cannot be invoked by a stale snapshot.");
    }

    [AvaloniaTest]
    public async Task Extension_lease_lasts_until_the_complete_async_operation_ends()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new ExtensionProvider();
        var editor = new EditorService(provider);
        var extension = new ClosedToolExtension { Operation = gate.Task };
        provider.AddExtensions(-43002, [extension]);
        var manager = new ContextCommandManager(new ContextCommandSettingsStore(), new ContextCommandHandlerRegistry());
        manager.Register(extension);
        var service = new CommandPaletteService(manager, new FixedHandlerProvider(null), () => null, editor, provider);
        Task operation = service.EnumerateCommands().Single().ExecuteAsync();
        Task drain = provider.RemoveExtensions(-43002).DrainAsync().AsTask();
        Assert.That(drain.IsCompleted, Is.False);
        gate.SetResult();
        await operation;
        await drain;
    }

    [AvaloniaTest]
    public async Task Extension_state_notifications_refresh_availability_and_release_on_close()
    {
        var provider = new ExtensionProvider();
        var editor = new EditorService(provider);
        var extension = new ClosedToolExtension { Enabled = false };
        provider.AddExtensions(-43003, [extension]);
        var manager = new ContextCommandManager(new ContextCommandSettingsStore(), new ContextCommandHandlerRegistry());
        manager.Register(extension);
        var service = new CommandPaletteService(manager, new FixedHandlerProvider(null), () => null, editor, provider);
        using var palette = new CommandPaletteViewModel(service, editor);
        palette.Open();
        Assert.That(palette.FilteredCommands.Single().IsEnabled, Is.False);
        extension.Enabled = true;
        extension.NotifyStateChanged();
        await UntilAsync(() => palette.FilteredCommands.Single().IsEnabled);

        Task drain = provider.RemoveExtensions(-43003).DrainAsync().AsTask();
        Assert.That(drain.IsCompleted, Is.False);
        palette.Close();
        await drain;
    }

    [AvaloniaTest]
    public async Task An_answered_prompt_hides_the_palette_while_the_command_works_and_a_later_step_reopens_it()
    {
        var work = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? first = null;
        string? second = null;
        var (palette, _) = CreatePalette(async execution =>
        {
            first = await execution.Interaction!.ShowInputAsync(new ContextCommandInputOptions());
            await work.Task;
            second = await execution.Interaction.ShowInputAsync(new ContextCommandInputOptions());
            await work.Task;
        });
        using (palette)
        {
            palette.Open();
            Task operation = palette.ExecuteSelectedAsync();
            HeadlessTestHelpers.Settle();

            palette.Query.Value = "first";
            await palette.ExecuteSelectedAsync();
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(first, Is.EqualTo("first"));
                Assert.That(palette.IsOpen.Value, Is.False, "The answered prompt must not stay on screen.");
                Assert.That(operation.IsCompleted, Is.False);
            });

            work.SetResult();
            await UntilAsync(() => palette.Prompt.Value is { IsCompleted: false });
            Assert.That(palette.IsOpen.Value, Is.True, "A later step reopens the palette.");

            palette.Query.Value = "second";
            await palette.ExecuteSelectedAsync();
            await operation;
            Assert.Multiple(() =>
            {
                Assert.That(second, Is.EqualTo("second"));
                Assert.That(palette.IsOpen.Value, Is.False);
                Assert.That(palette.Prompt.Value, Is.Null);
            });
        }
    }

    private sealed class PromptExtension : ViewExtension
    {
        public override IEnumerable<ContextCommandDefinition> ContextCommands =>
        [
            new("Prompt", "Prompt command")
        ];
    }

    private sealed class Handler(Func<ContextCommandExecution, Task> execute) : IContextCommandHandler
    {
        public Task ExecuteAsync(ContextCommandExecution execution) => execute(execution);
    }

    private sealed class FixedHandlerProvider(IContextCommandHandler? handler) : ICommandPaletteHandlerProvider
    {
        public IContextCommandHandler? Resolve(Type extensionType) => handler;
    }

    private sealed class ClosedToolExtension : ToolTabExtension, IContextCommandHandler, IContextCommandStateNotifier
    {
        private readonly Subject<Unit> _stateChanged = new();

        public override bool CanMultiple => false;
        public bool Enabled { get; set; } = true;
        public int Executions { get; private set; }
        public int ContextCreations { get; private set; }
        public ContextCommandExecution? Execution { get; private set; }
        public Task Operation { get; init; } = Task.CompletedTask;
        public IObservable<Unit> CanExecuteChanged => _stateChanged;

        public void NotifyStateChanged() => _stateChanged.OnNext(Unit.Default);

        public override IEnumerable<ContextCommandDefinition> ContextCommands =>
        [
            new("Global", "Closed tool command") { Scope = ContextCommandScope.Extension },
            new("Local", "Open tool command")
        ];

        public Task ExecuteAsync(ContextCommandExecution execution)
        {
            Executions++;
            Execution = execution;
            return Operation;
        }

        public bool CanExecute(ContextCommandExecution execution) => Enabled;

        public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
        {
            throw new InvalidOperationException("Enumerating commands must not create content.");
        }

        public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
        {
            ContextCreations++;
            throw new InvalidOperationException("Enumerating commands must not create contexts.");
        }
    }
}
