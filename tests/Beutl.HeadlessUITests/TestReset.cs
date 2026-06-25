using Beutl.Configuration;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// Shared global-state reset for tests that open editor tabs. Must run on the Avalonia UI thread
// (awaited at the start of each [AvaloniaTest] body), where touching ProjectService / EditorService /
// BeutlApplication is safe; NUnit [SetUp]/[TearDown] run off that thread.
internal static class TestReset
{
    public static async Task ResetShellAsync()
    {
        // Editor tabs are process-global and persist across tests. Their tool tabs (e.g. the file
        // browser) hold live FileSystemWatchers on BEUTL_HOME; left open, a watcher from a prior
        // test fires into a disposed view model when a later test writes under BEUTL_HOME. Dispose
        // the tabs so those watchers are torn down.
        await DisposeOpenEditorTabsAsync();

        // The test build reports BeutlApplication.Version "1.0.0" (no NuGetVersion metadata), so a
        // persisted minAppVersion looks newer and OpenProject would pop the version-mismatch dialog,
        // which needs a window the headless host lacks. SkipVersionCheck removes that branch.
        Preferences.Default.Set("ProjectService.SkipVersionCheck", true);

        TestShell.Project.CloseProject();
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
