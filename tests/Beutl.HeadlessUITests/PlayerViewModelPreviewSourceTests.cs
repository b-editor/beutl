using Beutl.Language;
using Beutl.Media.Proxy;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class PlayerViewModelPreviewSourceTests
{
    [Test]
    public void GetPreviewSourceLabel_maps_PreferProxy_to_the_proxy_label()
    {
        Assert.That(
            PlayerViewModel.GetPreviewSourceLabel(PreviewSourceMode.PreferProxy),
            Is.EqualTo(Strings.PreviewSourcePreferProxy));
    }

    [Test]
    public void GetPreviewSourceLabel_maps_ForceOriginal_to_the_original_label()
    {
        Assert.That(
            PlayerViewModel.GetPreviewSourceLabel(PreviewSourceMode.ForceOriginal),
            Is.EqualTo(Strings.PreviewSourceForceOriginal));
    }
}
