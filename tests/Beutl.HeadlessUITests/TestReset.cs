using Beutl.Configuration;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// Shared global-state reset for tests that open editor tabs. Must run on the Avalonia UI thread
// (as the first line of each [AvaloniaTest] body), where touching ProjectService / EditorService /
// BeutlApplication is safe; NUnit [SetUp]/[TearDown] run off that thread.
internal static class TestReset
{
    public static void ResetShell()
    {
        // Editor tabs are process-global and persist across tests. Their tool tabs (e.g. the file
        // browser) hold live FileSystemWatchers on BEUTL_HOME; left open, a watcher from a prior
        // test fires into a disposed view model when a later test writes under BEUTL_HOME. Dispose
        // the tabs so those watchers are torn down.
        DisposeOpenEditorTabs();

        // The test build reports BeutlApplication.Version "1.0.0" (no NuGetVersion metadata), so a
        // persisted minAppVersion looks newer and OpenProject would pop the version-mismatch dialog,
        // which needs a window the headless host lacks. SkipVersionCheck removes that branch.
        Preferences.Default.Set("ProjectService.SkipVersionCheck", true);

        ProjectService.Current.CloseProject();
        BeutlApplication.Current.Items.Clear();
        HeadlessTestHelpers.Settle();
    }

    private static void DisposeOpenEditorTabs()
    {
        EditorService.Current.SelectedTabItem.Value = null;
        foreach (EditorTabItem tab in EditorService.Current.TabItems.ToArray())
        {
            EditorService.Current.CloseTabItem(tab).GetAwaiter().GetResult();
        }

        HeadlessTestHelpers.Settle();
    }
}
