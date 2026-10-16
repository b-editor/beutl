using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class ContextCommandHandlerTests
{
    [AvaloniaTest]
    public async Task A_key_and_the_palette_run_a_typed_command_on_the_open_context()
    {
        var counter = new Counter();
        var extension = new CounterExtension();
        var manager = new ContextCommandManager(new ContextCommandSettingsStore(new JsonObject(), persist: false));
        manager.Register(extension);
        var element = new Border { DataContext = counter };
        manager.Attach(element, extension);
        var provider = new ExtensionProvider();
        var palette = new CommandPaletteService(
            manager, new CommandPaletteTestHost.FixedHandlerProvider(counter), () => null, new EditorService(provider), provider);

        var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F5 };
        element.RaiseEvent(key);
        HeadlessTestHelpers.Settle();
        await palette.EnumerateCommands().Single(command => command.DisplayName == "Count").ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(counter.Count, Is.EqualTo(2));
            Assert.That(key.Handled, Is.True, "A command that runs uses the key.");
        });
    }

    [AvaloniaTest]
    public void A_key_for_a_command_that_cannot_run_stays_unhandled()
    {
        var counter = new Counter { CanCount = false };
        var extension = new CounterExtension();
        var manager = new ContextCommandManager(new ContextCommandSettingsStore(new JsonObject(), persist: false));
        manager.Register(extension);
        var element = new Border { DataContext = counter };
        manager.Attach(element, extension);

        var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F5 };
        element.RaiseEvent(key);
        HeadlessTestHelpers.Settle();

        Assert.That((counter.Count, key.Handled), Is.EqualTo((0, false)));
    }

    private sealed class Counter
    {
        public int Count { get; set; }

        public bool CanCount { get; init; } = true;
    }

    private sealed class CounterExtension : ViewExtension
    {
        public override IEnumerable<ContextCommandDefinition> ContextCommands =>
        [
            new ContextCommandDefinition<Counter>("Count", "Count", null, [new ContextCommandKeyGesture("F5")])
            {
                Id = "test.count",
                CanExecuteOn = (counter, _) => counter.CanCount,
                ExecuteOnAsync = (counter, _) =>
                {
                    counter.Count++;
                    return Task.CompletedTask;
                }
            },
        ];
    }
}
