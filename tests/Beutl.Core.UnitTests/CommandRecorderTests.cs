using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Beutl.Core.UnitTests;

public class CommandRecorderTests
{
    [SetUp]
    public void Setup()
    {
        BeutlApplication.Current.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
    }

    [Test]
    public void CanUndo_ReturnsFalse_WhenRecorderIsEmpty()
    {
        // Arrange
        var recorder = new CommandRecorder();

        // Act
        bool canUndo = recorder.CanUndo;

        // Assert
        Assert.False(canUndo);
    }

    [Test]
    public void CanRedo_ReturnsFalse_WhenRecorderIsEmpty()
    {
        // Arrange
        var recorder = new CommandRecorder();

        // Act
        bool canRedo = recorder.CanRedo;

        // Assert
        Assert.False(canRedo);
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
        Assert.True(recorder.CanUndo);
        Assert.False(recorder.CanRedo);
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
        Assert.True(recorder.CanUndo);
        Assert.False(recorder.CanRedo);
        Assert.True(command.IsExecuted);
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
        Assert.False(recorder.CanUndo);
        Assert.True(recorder.CanRedo);
        Assert.False(command.IsExecuted);
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
        Assert.True(recorder.CanUndo);
        Assert.False(recorder.CanRedo);
        Assert.True(command.IsExecuted);
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
        Assert.False(recorder.CanUndo);
        Assert.False(recorder.CanRedo);
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
