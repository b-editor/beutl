using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Language;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

// Asserts arranged layout, visibility and hit testing only (no pixel readback): frame capture of an
// inflated shell view crashes the headless host on software Vulkan.
[TestFixture, NonParallelizable]
public sealed class ProjectLifecycleOverlayTests
{
    [AvaloniaTest]
    public async Task Lifecycle_activity_replaces_the_editor_area_with_progress_until_it_ends()
    {
        await TestReset.ResetShellAsync();
        var mainView = new MainView { DataContext = TestShell.MainViewModel };
        var window = new Window { Width = 1000, Height = 700 };
        IDisposable? creating = null;
        IDisposable? closing = null;
        try
        {
            // Attached after the window opens, so the shell's window-opened setup, which needs the real
            // app, does not run.
            window.Show();
            window.Content = mainView;
            HeadlessTestHelpers.Render();
            Border overlay = mainView.FindControl<Border>("ProjectLifecycleOverlay")!;
            TextBlock title = mainView.FindControl<TextBlock>("ProjectLifecycleTitle")!;
            TextBlock message = mainView.FindControl<TextBlock>("ProjectLifecycleMessage")!;
            EditorHostFallback fallback = mainView.GetVisualDescendants()
                .OfType<EditorHostFallback>()
                .Single();
            // The hidden overlay has no layout yet; the start page fills the same editor area.
            Point center = fallback.TranslatePoint(
                new Point(fallback.Bounds.Width / 2, fallback.Bounds.Height / 2),
                window)!.Value;

            Assert.Multiple(() =>
            {
                Assert.That(overlay.IsVisible, Is.False);
                Assert.That(fallback.IsEffectivelyVisible, Is.True);
                Assert.That(fallback.IsEffectivelyEnabled, Is.True);
            });

            creating = TestShell.Editor.BeginLifecycleActivity(ProjectLifecycleActivity.CreatingProject);
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(overlay.IsEffectivelyVisible, Is.True);
                Assert.That(title.Text, Is.EqualTo(MessageStrings.CreatingProject));
                Assert.That(message.Text, Is.EqualTo(MessageStrings.CreatingProjectWithVersionControlMessage));
                Assert.That(fallback.IsEffectivelyEnabled, Is.False);
                Assert.That(IsWithin(window.InputHitTest(center), overlay), Is.True);
            });

            // Creating a project first closes the open one, so the later activity is shown on top.
            closing = TestShell.Editor.BeginLifecycleActivity(ProjectLifecycleActivity.ClosingProject);
            HeadlessTestHelpers.Render();
            Assert.That(title.Text, Is.EqualTo(MessageStrings.ClosingProject));

            closing.Dispose();
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(overlay.IsEffectivelyVisible, Is.True);
                Assert.That(title.Text, Is.EqualTo(MessageStrings.CreatingProject));
            });

            creating.Dispose();
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(overlay.IsVisible, Is.False);
                Assert.That(fallback.IsEffectivelyEnabled, Is.True);
                Assert.That(IsWithin(window.InputHitTest(center), fallback), Is.True);
            });
        }
        finally
        {
            closing?.Dispose();
            creating?.Dispose();
            window.Close();
            mainView.DataContext = null;
            HeadlessTestHelpers.Settle();
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public void Lifecycle_activity_can_end_from_a_worker_thread()
    {
        var editorService = new EditorService(new Beutl.Api.Services.ExtensionProvider());
        IDisposable activity = editorService.BeginLifecycleActivity(ProjectLifecycleActivity.ClosingProject);
        Assert.That(
            editorService.LifecycleActivity.Value,
            Is.EqualTo(ProjectLifecycleActivity.ClosingProject));

        Task.Run(activity.Dispose).GetAwaiter().GetResult();
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(
                editorService.LifecycleActivity.Value,
                Is.EqualTo(ProjectLifecycleActivity.None));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                editorService.BeginLifecycleActivity(ProjectLifecycleActivity.None));
        });
    }

    private static bool IsWithin(IInputElement? element, Visual ancestor)
    {
        return element is Visual visual
               && (ReferenceEquals(visual, ancestor) || visual.GetVisualAncestors().Contains(ancestor));
    }
}
