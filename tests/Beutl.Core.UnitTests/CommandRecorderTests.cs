using Microsoft.Extensions.Logging;

using NUnit.Framework;

using Beutl.Logging;

namespace Beutl.Core.UnitTests;

public class CommandRecorderTests
{
    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
    }

    [Test]
    public void CanUndo_ReturnsFalse_WhenRecorderIsEmpty()
    {
        // Arrange
        var recorder = new CommandRecorder();

        // Act
        bool canUndo = recorder.CanUndo;

        // Assert
        Assert.That(!canUndo);
    }

    [Test]
    public void CanRedo_ReturnsFalse_WhenRecorderIsEmpty()
    {
        // Arrange
        var recorder = new CommandRecorder();

        // Act
        bool canRedo = recorder.CanRedo;

        // Assert
        Assert.That(!canRedo);
    }

    [Test]
    public void PushOnly_AddsCommandToTheRecorder()
    {
        // Arrange
        var recorder = new CommandRecorder();
        var command = new TestCommand();

        // Act
        recorder.PushOnly(command);

        // Assert
        Assert.That(recorder.CanUndo);
        Assert.That(!recorder.CanRedo);
    }

    [Test]
    public void DoAndPush_AddsCommandToTheRecorderAndExecutesIt()
    {
        // Arrange
        var recorder = new CommandRecorder();
        var command = new TestCommand();

        // Act
        recorder.DoAndPush(command);

        // Assert
        Assert.That(recorder.CanUndo);
        Assert.That(!recorder.CanRedo);
        Assert.That(command.IsExecuted);
    }

    [Test]
    public void Undo_RollsBackTheLastCommand()
    {
        // Arrange
        var recorder = new CommandRecorder();
        var command = new TestCommand();
        recorder.DoAndPush(command);

        // Act
        recorder.Undo();

        // Assert
        Assert.That(!recorder.CanUndo);
        Assert.That(recorder.CanRedo);
        Assert.That(!command.IsExecuted);
    }

    [Test]
    public void Redo_RerunsTheLastUndoneCommand()
    {
        // Arrange
        var recorder = new CommandRecorder();
        var command = new TestCommand();
        recorder.DoAndPush(command);
        recorder.Undo();

        // Act
        recorder.Redo();

        // Assert
        Assert.That(recorder.CanUndo);
        Assert.That(!recorder.CanRedo);
        Assert.That(command.IsExecuted);
    }

    [Test]
    public void Clear_RemovesAllCommandsFromTheRecorder()
    {
        // Arrange
        var recorder = new CommandRecorder();
        var command = new TestCommand();
        recorder.DoAndPush(command);

        // Act
        recorder.Clear();

        // Assert
        Assert.That(!recorder.CanUndo);
        Assert.That(!recorder.CanRedo);
    }

    private class TestCommand : IRecordableCommand
    {
        public bool IsExecuted { get; private set; }

        public void Do()
        {
            IsExecuted = true;
        }

        public void Undo()
        {
            IsExecuted = false;
        }

        public void Redo()
        {
            IsExecuted = true;
        }
    }
}
