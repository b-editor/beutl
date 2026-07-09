using Beutl.Editor.Components.TimelineTab.ViewModels;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class LayerHeaderEnabledStateTests
{
    [Test]
    public void AllEditableReachedTarget_ClaimsState()
    {
        bool result = LayerHeaderViewModel.ShouldClaimEnabledState(
            [(IsEditable: true, IsEnabled: false), (IsEditable: true, IsEnabled: false)], target: false);

        Assert.That(result, Is.True);
    }

    [Test]
    public void MixedLockRow_IgnoresLockedClip_ClaimsWhenEditableReachedTarget()
    {
        // Free clip toggled to false; locked clip keeps IsEnabled == true. Counting the
        // locked clip would falsely revert the header even though the real change landed.
        bool result = LayerHeaderViewModel.ShouldClaimEnabledState(
            [(IsEditable: false, IsEnabled: true), (IsEditable: true, IsEnabled: false)], target: false);

        Assert.That(result, Is.True);
    }

    [Test]
    public void EditableClipDidNotReachTarget_DoesNotClaim()
    {
        bool result = LayerHeaderViewModel.ShouldClaimEnabledState(
            [(IsEditable: true, IsEnabled: false), (IsEditable: true, IsEnabled: true)], target: false);

        Assert.That(result, Is.False);
    }

    [Test]
    public void AllClipsLocked_DoesNotClaim()
    {
        bool result = LayerHeaderViewModel.ShouldClaimEnabledState(
            [(IsEditable: false, IsEnabled: true)], target: false);

        Assert.That(result, Is.False);
    }

    [Test]
    public void EmptyRow_DoesNotClaim()
    {
        bool result = LayerHeaderViewModel.ShouldClaimEnabledState([], target: false);

        Assert.That(result, Is.False);
    }
}
