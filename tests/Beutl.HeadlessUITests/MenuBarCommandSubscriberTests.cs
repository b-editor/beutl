using System.Reflection;
using System.Windows.Input;
using Avalonia.Headless.NUnit;
using Beutl.ViewModels;
using Beutl.Views;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

// Guards against another #2857: a menu bar command with no subscriber does nothing from its menu item,
// shortcut or palette entry. MainView subscribes the commands that open dialogs.
[TestFixture, NonParallelizable]
public sealed class MenuBarCommandSubscriberTests
{
    [AvaloniaTest]
    public async Task Every_menu_bar_command_has_a_subscriber_once_MainView_is_built()
    {
        // A fresh view model, so a MainView that another test left attached cannot hide a missing subscriber.
        var viewModel = new MainViewModel();
        var view = new MainView { DataContext = viewModel };
        try
        {
            PropertyInfo[] commands = typeof(MenuBarViewModel)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => typeof(ICommand).IsAssignableFrom(property.PropertyType))
                .ToArray();

            Assert.That(commands, Is.Not.Empty);
            Assert.That(
                commands.Where(property => !HasSubscriber((ICommand)property.GetValue(viewModel.MenuBar)!))
                    .Select(property => property.Name),
                Is.Empty,
                "Running these commands does nothing.");
        }
        finally
        {
            view.DataContext = null;
            viewModel.Dispose();
            await viewModel.WaitForDisposalAsync();
        }
    }

    [Test]
    public void Subscriber_check_follows_subscribe_and_dispose()
    {
        var asyncCommand = new AsyncReactiveCommand();
        var slimCommand = new ReactiveCommandSlim<string>();
        ICommand[] commands = [asyncCommand, slimCommand];
        Assert.That(commands.Select(HasSubscriber), Is.All.False);

        IDisposable asyncSubscription = asyncCommand.Subscribe(() => Task.CompletedTask);
        IDisposable slimSubscription = slimCommand.Subscribe(_ => { });
        Assert.That(commands.Select(HasSubscriber), Is.All.True);

        asyncSubscription.Dispose();
        slimSubscription.Dispose();
        Assert.That(commands.Select(HasSubscriber), Is.All.False);
    }

    // ReactiveProperty does not expose a command's subscribers, so they are read from its private fields.
    private static bool HasSubscriber(ICommand command)
    {
        for (Type? type = command.GetType(); type is not null; type = type.BaseType)
        {
            if (!type.IsGenericType)
                continue;

            Type definition = type.GetGenericTypeDefinition();
            if (definition == typeof(AsyncReactiveCommand<>))
            {
                // ReactiveProperty's internal ImmutableList, whose Data array holds the subscribed actions.
                object actions = Field(type, "_asyncActions")!;
                return ((Array)actions.GetType().GetProperty("Data")!.GetValue(actions)!).Length > 0;
            }

            if (definition == typeof(ReactiveCommandSlim<>))
                return Field(type, "_root") is not null;
        }

        throw new NotSupportedException($"{command.GetType()} is not a command type this test can inspect.");

        object? Field(Type type, string name) =>
            (type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingFieldException(type.FullName, name)).GetValue(command);
    }
}
