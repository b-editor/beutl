using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

// A seek from another thread reaches the editor-clock bridge on the UI thread, so it cannot race the bridge's disposal.
[TestFixture]
public class EditorViewModelDisposeRaceTests
{
    // Supplies the services accepted by the editor property context.
    private sealed class ServiceVisitor(EditViewModel editViewModel, IEditorClock clock)
        : IPropertyEditorContextVisitor, IServiceProvider
    {
        public void Visit(IPropertyEditorContext context)
        {
        }

        public object? GetService(Type serviceType)
            => serviceType == typeof(IEditorClock) ? clock : editViewModel.GetService(serviceType);
    }

    [AvaloniaTest]
    public async Task A_seek_from_another_thread_reaches_the_clock_bridge_on_the_UI_thread_and_not_after_dispose()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("dispose-race");
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        var adapter = new EnginePropertyAdapter<TimeSpan>(editor.Scene.DurationProperty, editor.Scene);
        var viewModel = new ValueEditorViewModel<TimeSpan>(adapter);
        viewModel.Accept(new ServiceVisitor(editor, clock));
        var bridged = new List<(TimeSpan Time, bool OnUIThread)>();
        using IDisposable subscription = viewModel.CurrentTime.Subscribe(
            time => bridged.Add((time, Dispatcher.UIThread.CheckAccess())));

        await Task.Run(() => clock.Seek(TimeSpan.FromSeconds(1)));
        HeadlessTestHelpers.Settle();
        viewModel.Dispose();
        await Task.Run(() => clock.Seek(TimeSpan.FromSeconds(2)));
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(clock.CurrentTime.Value, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(bridged.Select(b => b.Time),
                Does.Contain(TimeSpan.FromSeconds(1)).And.Not.Contain(TimeSpan.FromSeconds(2)));
            Assert.That(bridged.Select(b => b.OnUIThread), Is.All.True);
        });
    }
}
