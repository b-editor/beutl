using System.Windows.Input;
using Avalonia.Input;
using Beutl.Editor.Components.VersionControlTab.Views;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class VersionControlTabViewTests
{
    [TestCase(Key.Return, KeyModifiers.Control)]
    [TestCase(Key.Enter, KeyModifiers.Control)]
    [TestCase(Key.Return, KeyModifiers.Meta)]
    [TestCase(Key.Enter, KeyModifiers.Meta)]
    public void Commit_shortcut_executes_enabled_command(
        Key key,
        KeyModifiers modifiers)
    {
        var command = new RecordingCommand(canExecute: true);

        bool handled = VersionControlTabView.TryExecuteCommitShortcut(
            key,
            modifiers,
            command);

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(command.ExecuteCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Enter_without_commit_modifier_is_not_handled()
    {
        var command = new RecordingCommand(canExecute: true);

        bool handled = VersionControlTabView.TryExecuteCommitShortcut(
            Key.Return,
            KeyModifiers.None,
            command);

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.False);
            Assert.That(command.ExecuteCount, Is.Zero);
        });
    }

    [Test]
    public void Disabled_commit_shortcut_is_handled_without_executing()
    {
        var command = new RecordingCommand(canExecute: false);

        bool handled = VersionControlTabView.TryExecuteCommitShortcut(
            Key.Return,
            KeyModifiers.Control,
            command);

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(command.ExecuteCount, Is.Zero);
        });
    }

    private sealed class RecordingCommand(bool canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public int ExecuteCount { get; private set; }

        public bool CanExecute(object? parameter)
        {
            return canExecute;
        }

        public void Execute(object? parameter)
        {
            ExecuteCount++;
        }
    }
}
