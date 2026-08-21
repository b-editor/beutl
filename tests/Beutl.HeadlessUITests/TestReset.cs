using Avalonia;
using Beutl.Configuration;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// Shell-state reset for tests that perform multiple project operations. Must run on the Avalonia UI
// thread (awaited inside each [AvaloniaTest] body), where touching ProjectService / EditorService /
// BeutlApplication is safe; NUnit [SetUp]/[TearDown] run off that thread. The suite runs at
// PerAssembly isolation, so one TestApp — and one MainViewModel — is shared by every test case.
internal static class TestReset
{
    public static async Task ResetShellAsync()
    {
        // Before anything resolves the shell: a case that disposed the shared MainViewModel would
        // otherwise hand this reset, and every later case, a torn-down composition root.
        ((TestApp)Application.Current!).DropMainViewModelIfDisposed();

        // Editor tabs can outlive an earlier project operation within the current test. Their tool
        // tabs (e.g. the file browser) hold live FileSystemWatchers on BEUTL_HOME, so dispose them
        // before resetting the project and application items.
        await DisposeOpenEditorTabsAsync();

        // The test build reports BeutlApplication.Version "1.0.0" (no NuGetVersion metadata), so a
        // persisted minAppVersion looks newer and OpenProject would pop the version-mismatch dialog,
        // which needs a window the headless host lacks. SkipVersionCheck removes that branch.
        Preferences.Default.Set("ProjectService.SkipVersionCheck", true);

        // A case that exercised shutdown latched the request on the ProjectService this assembly
        // shares, and every transition below (and in every later case) is rejected while it is set.
        TestShell.Project.ClearShutdownRequest();

        await TestShell.Project.CloseProject();
        BeutlApplication.Current.Items.Clear();
        HeadlessTestHelpers.Settle();
    }

    // Awaited rather than blocked: tab disposal pauses the player, which awaits the playback task;
    // blocking the UI thread on it would deadlock against playback callbacks posted to the dispatcher.
    private static async Task DisposeOpenEditorTabsAsync()
    {
        TestShell.Editor.SelectedTabItem.Value = null;
        foreach (EditorTabItem tab in TestShell.Editor.TabItems.ToArray())
        {
            await TestShell.Editor.CloseTabItem(tab);
        }

        HeadlessTestHelpers.Settle();
    }
}
