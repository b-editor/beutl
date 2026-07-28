using System.Globalization;
using System.Resources;
using Beutl.Language;

namespace Beutl.UnitTests.Language;

[TestFixture]
public class VersionControlStringsTests
{
    private static readonly string[] s_requiredKeys =
    [
        "VersionControl",
        "VersionControl_Enable",
        "VersionControl_UntrackedExplanation",
        "VersionControl_DownloadGit",
        "VersionControl_TrackHistory",
        "VersionControl_Commit",
        "VersionControl_Restore",
        "VersionControl_SnapshotSave",
        "VersionControl_GitNotInstalled",
        "VersionControl_ConflictGuidance",
        "VersionControl_RestoreConfirmation",
        "VersionControl_ExportInProgress",
        "VersionControl_RecoveryFailed",
        "VersionControl_BranchFormat",
        "VersionControl_AheadBehindFormat",
        "VersionControl_DirtySummaryFormat",
        "VersionControl_WorktreeClean",
        "VersionControl_LoadMore",
        "VersionControl_Diff",
        "VersionControl_NoRepository",
        "VersionControl_NoHistory",
        "VersionControl_HistoryEmptyHint",
        "VersionControl_ChangedFilesEmptyHint",
        "VersionControl_DiffEmptyHint",
        "VersionControl_TimeJustNow",
        "VersionControl_TimeMinuteAgo",
        "VersionControl_TimeMinutesAgoFormat",
        "VersionControl_TimeHourAgo",
        "VersionControl_TimeHoursAgoFormat",
        "VersionControl_TimeDayAgo",
        "VersionControl_TimeDaysAgoFormat",
        "VersionControl_BranchName",
        "VersionControl_CreateBranchTitle",
        "VersionControl_CommitNow",
        "VersionControl_CommitCreated",
        "VersionControl_CreateBranch",
        "VersionControl_Switch",
        "VersionControl_SwitchBranch",
        "VersionControl_SwitchBranchConfirmation",
        "VersionControl_PullConfirmation",
        "VersionControl_EnclosingRepositoryScopeFormat",
        "VersionControl_Remote",
        "VersionControl_RemoteUrl",
        "VersionControl_SetRemote",
        "VersionControl_RemoteConnected",
        "VersionControl_Pushing",
        "VersionControl_Pulling",
        "VersionControl_RemoteOperationSucceeded",
        "VersionControl_RemoteOperationCanceled",
        "VersionControl_LfsQuotaNotice",
        "VersionControl_LargeMediaWarningFormat",
        "VersionControl_AuthenticationFailed",
        "VersionControl_Diverged",
        "VersionControl_Offline",
    ];

    [TestCase("en-US")]
    [TestCase("ja-JP")]
    public void Required_version_control_strings_are_localized(string cultureName)
    {
        var resourceManager = new ResourceManager("Beutl.Language.Strings", typeof(Strings).Assembly);
        CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);

        Assert.Multiple(() =>
        {
            foreach (string key in s_requiredKeys)
            {
                Assert.That(resourceManager.GetString(key, culture), Is.Not.Null.And.Not.Empty, key);
            }
        });
    }
}
