using Beutl.Editor.VersionControl;

namespace Beutl.UnitTests.Editor.VersionControl;

[TestFixture]
public class ProjectPathRulesTests
{
    [TestCase(".beutl/view-state/scene.json", true)]
    [TestCase("scenes/.BEUTL/output-profile.json", true)]
    [TestCase("scene.bep.0123abcd.TMP", true)]
    [TestCase("folder.tmp", true)]
    [TestCase("project.bep", false)]
    [TestCase("beutl/clip.scene", false)]
    [TestCase(".beutl-cache/clip.scene", false)]
    [TestCase("tmp", false)]
    public void IsReserved_matches_the_state_folder_and_scratch_files_in_any_case(
        string projectRelativePath,
        bool expected)
    {
        Assert.That(ProjectPathRules.IsReserved(projectRelativePath), Is.EqualTo(expected));
    }
}
