using Beutl.Configuration;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// Shell-state reset for tests that perform multiple project operations. Must run on the Avalonia UI
// thread (awaited inside each [AvaloniaTest] body), where touching ProjectService / EditorService /
// BeutlApplication is safe; NUnit [SetUp]/[TearDown] run off that thread. Avalonia.Headless creates a
// fresh TestApp per [AvaloniaTest], so the MainViewModel itself is not shared between test cases.
internal static class TestReset
{
    public static async Task ResetShellAsync()
    {
        // Editor tabs can outlive an earlier project operation within the current test. Their tool
        // tabs (e.g. the file browser) hold live FileSystemWatchers on BEUTL_HOME, so dispose them
        // before resetting the project and application items.
        await DisposeOpenEditorTabsAsync();

        // The test build reports BeutlApplication.Version "1.0.0" (no NuGetVersion metadata), so a
        // persisted minAppVersion looks newer and OpenProject would pop the version-mismatch dialog,
        // which needs a window the headless host lacks. SkipVersionCheck removes that branch.
        Preferences.Default.Set("ProjectService.SkipVersionCheck", true);

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
