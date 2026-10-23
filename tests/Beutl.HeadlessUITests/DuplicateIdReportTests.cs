using Avalonia.Headless.NUnit;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beutl.HeadlessUITests;

// Saving a scene and closing the project count the duplicate Ids that edits made, as reading a project does
// (b-editor/beutl#2885).
[TestFixture, NonParallelizable]
public sealed class DuplicateIdReportTests
{
    [AvaloniaTest]
    public async Task Saving_the_scene_and_closing_the_project_log_the_duplicate_ids_made_since()
    {
        if (!Log.IsLoggerFactoryConfigured)
            Log.LoggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        using var logs = new LogRecorder();
        Log.LoggerFactory.AddProvider(logs);
        (Project project, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("duplicate-ids");
        try
        {
            // An object added with another's Id, as a copy that keeps its Ids would be.
            Element first = AddElement(scene);
            AddElement(scene, first.Id.Value);

            Assert.That(await editor.SaveAsync(), Is.True);
            AddElement(scene, first.Id.Value);
            // Without saving, so that the close finds what the save did not.
            Assert.That(
                await TestShell.Project.TryCloseProjectAsync(project, ProjectService.ProjectCloseIntent.DiscardChanges),
                Is.True);

            Assert.That(logs.Messages, Has.Count.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(logs.Messages[0], Does.Contain("on save: 1 object(s)"));
                Assert.That(logs.Messages[1], Does.Contain("on close: 2 object(s)"));
            });
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    private static Element AddElement(Scene scene, Guid? id = null)
    {
        var element = new Element { Start = { Value = MediaTime.FromSeconds(scene.Children.Count) }, Length = { Value = MediaTime.FromSeconds(1) } };
        if (id is { } given)
            element.Id.Value = given;
        string file = $"{Guid.NewGuid():N}.belm";
        element.Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, file));
        scene.Children.Add(element);
        HeadlessTestHelpers.Settle();
        return element;
    }

    // Records the messages logged under DuplicateIds until disposed.
    private sealed class LogRecorder : ILoggerProvider, ILogger
    {
        private bool _disposed;

        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName)
            => categoryName == typeof(DuplicateIds).FullName ? this : NullLogger.Instance;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => !_disposed && logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Messages.Add(formatter(state, exception));
        }

        public void Dispose() => _disposed = true;
    }
}
