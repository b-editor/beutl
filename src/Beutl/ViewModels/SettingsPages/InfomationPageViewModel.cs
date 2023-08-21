#pragma warning disable CS0436

using Beutl.Controls.Navigation;

namespace Beutl.ViewModels.SettingsPages;

public sealed class InfomationPageViewModel : PageContext
{
    public string CurrentVersion { get; } = GitVersionInformation.SemVer;

    public string BuildMetadata { get; } = GitVersionInformation.FullBuildMetaData;

    public string GitRepositoryUrl { get; } = "https://github.com/b-editor/beutl";

    public string LicenseUrl { get; } = "https://github.com/b-editor/beutl/blob/main/LICENSE";

    public string ThirdPartyNoticesUrl { get; } = "https://github.com/b-editor/beutl/blob/main/THIRD_PARTY_NOTICES.md";
}
