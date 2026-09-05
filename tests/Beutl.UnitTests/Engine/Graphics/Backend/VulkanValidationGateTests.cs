using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

[TestFixture]
public sealed class VulkanValidationGateTests
{
    [Test]
    public void ARecordedError_IsCountedAndDescribedAgainstAnEarlierSnapshot()
    {
        var log = new VulkanValidationErrorLog();
        int before = log.Count;

        log.Record("VUID-vkCmdBeginRenderPass-None: a render pass is already recording");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(log.Count, Is.EqualTo(before + 1));
            Assert.That(log.DescribeSince(before), Does.Contain("VUID-vkCmdBeginRenderPass-None"));
            Assert.That(log.DescribeSince(log.Count), Is.Empty, "nothing arrived after the later snapshot");
        }
    }

    [Test]
    public void AnEmptyMessage_IsStillCounted()
    {
        var log = new VulkanValidationErrorLog();

        log.Record(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(log.Count, Is.EqualTo(1));
            Assert.That(log.DescribeSince(0), Does.Contain("no message text"));
        }
    }

    [Test]
    public void AFloodOfErrors_KeepsAnExactCountAndBoundedText()
    {
        var log = new VulkanValidationErrorLog();

        for (int index = 0; index < 200; index++)
            log.Record($"error-{index}");

        string description = log.DescribeSince(0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(log.Count, Is.EqualTo(200));
            Assert.That(log.Messages, Has.Count.LessThan(200));
            Assert.That(log.Messages, Has.Count.GreaterThan(0));
            Assert.That(description, Does.Contain("200 Vulkan validation error(s)"));
            Assert.That(description, Does.Contain("error-199"), "the newest error must be quoted");
            Assert.That(description, Does.Not.Contain("error-0"), "the oldest is dropped, not the newest");
        }
    }

    [Test]
    public void TheDescriptionQuotesOnlyWhatArrivedAfterTheSnapshot()
    {
        var log = new VulkanValidationErrorLog();
        log.Record("before-the-snapshot");
        int snapshot = log.Count;
        log.Record("after-the-snapshot");

        string description = log.DescribeSince(snapshot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(description, Does.Contain("after-the-snapshot"));
            Assert.That(description, Does.Not.Contain("before-the-snapshot"));
        }
    }

    [Test]
    public void AValidationErrorReportedDuringAnInvocation_FailsThatInvocation()
    {
        Assert.That(
            () => VulkanTestEnvironment.InvokeOnRenderThread(
                () => VulkanTestEnvironment.RecordDeliberateValidationError(
                    "VUID-synthetic-gate-probe: undefined behaviour")),
            Throws.InstanceOf<AssertionException>()
                .With.Message.Contains("VUID-synthetic-gate-probe"));
    }

    [Test]
    public void AnInvocationThatReportsNothing_Passes()
    {
        Assert.That(() => VulkanTestEnvironment.InvokeOnRenderThread(static () => { }), Throws.Nothing);
    }

    [Test]
    public void WhenTheJobAsksForValidation_TheInstanceEnabledIt()
    {
        if (!GraphicsContextFactory.IsVulkanValidationEnabled())
        {
            Assert.Ignore(
                "Validation was not requested (BEUTL_VULKAN_VALIDATION is unset), so the gate is idle and "
                + "there is nothing to confirm.");
        }

        // Deliberately not EnsureAvailable(): that reports an unusable Vulkan as a skip, which is the very
        // outcome this test exists to reject once validation has been asked for.
        VulkanTestEnvironment.EnsureInitialized();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                VulkanTestEnvironment.IsAvailable,
                Is.True,
                "Validation was requested but the Vulkan context could not be created, so every GPU test "
                + "would skip and the gate would observe nothing: "
                + (VulkanTestEnvironment.UnavailableReason ?? "(no reason recorded)"));
            Assert.That(
                GraphicsContextFactory.VulkanInstance?.EnableValidation,
                Is.True,
                "BEUTL_VULKAN_VALIDATION is set, so the instance must carry the validation layer and its "
                + "debug messenger; otherwise nothing reports to the gate.");
        }
    }
}
