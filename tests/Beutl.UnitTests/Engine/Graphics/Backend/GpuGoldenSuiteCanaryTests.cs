namespace Beutl.UnitTests.Engine.Graphics.Backend;

// The feature-003 visual-fidelity guards (the Golden/ + Backend/ Vulkan suites) all call
// VulkanTestEnvironment.EnsureAvailable(), which Assert.Ignore's — a SKIP that reads as "passed" — when no
// Vulkan ICD is present, hiding the loss of GPU coverage. This non-gated canary makes that skip observable;
// set BEUTL_REQUIRE_GPU=1 on a GPU-capable job to turn the silent skip into a hard failure.
[TestFixture]
public class GpuGoldenSuiteCanaryTests
{
    [Test]
    public void GpuGoldenSuite_RunsForReal_OrSkipIsExplicitlyAllowed()
    {
        if (VulkanTestEnvironment.IsAvailable)
        {
            Assert.Pass("Vulkan is available — the GPU golden suite runs for real.");
            return;
        }

        string reason = VulkanTestEnvironment.UnavailableReason ?? "Vulkan unavailable.";
        string? require = Environment.GetEnvironmentVariable("BEUTL_REQUIRE_GPU");
        bool required = string.Equals(require, "1", StringComparison.Ordinal)
                        || string.Equals(require, "true", StringComparison.OrdinalIgnoreCase);

        if (required)
        {
            Assert.Fail(
                "BEUTL_REQUIRE_GPU is set but Vulkan is unavailable, so the entire GPU golden-fidelity suite is "
                + "being silently skipped (provision MoltenVK / lavapipe / SwiftShader on this job): " + reason);
        }

        Assert.Ignore(
            "GPU golden-fidelity suite skipped because Vulkan is unavailable: " + reason
            + " — set BEUTL_REQUIRE_GPU=1 on a GPU-capable CI job to enforce it.");
    }

    // BEUTL_REQUIRE_GPU only escalates device PRESENCE above. The Vulkan-compute counter/gate surface (PrimitivePassTests'
    // FlushSyncs==2, C8 K-dispatch GpuPasses, compute alloc/scratch failure) additionally needs compute CAPABILITY
    // (Supports3DRendering); a context that is present but not compute-capable would silently skip that whole surface.
    // This canary makes that second skip observable under the same flag.
    [Test]
    public void ComputeCapability_RunsForReal_OrSkipIsExplicitlyAllowed()
    {
        if (!VulkanTestEnvironment.IsAvailable)
        {
            Assert.Ignore(
                "Compute-capability canary skipped because Vulkan is unavailable: "
                + (VulkanTestEnvironment.UnavailableReason ?? "Vulkan unavailable.")
                + " — the device-presence canary already gates this.");
            return;
        }

        var context = VulkanTestEnvironment.EnsureAvailable();
        if (context.Supports3DRendering)
        {
            Assert.Pass("Vulkan is compute-capable — the runtime compute counter/gate surface runs for real.");
            return;
        }

        if (VulkanTestEnvironment.IsGpuRequired)
        {
            Assert.Fail(
                "BEUTL_REQUIRE_GPU is set but the Vulkan context is not compute-capable (Supports3DRendering == "
                + "false), so the runtime compute counter/gate surface is being silently skipped (provision a "
                + "compute-capable ICD on this job).");
        }

        Assert.Ignore(
            "Vulkan compute counter/gate surface skipped because the context is not compute-capable "
            + "(Supports3DRendering == false) — set BEUTL_REQUIRE_GPU=1 on a compute-capable CI job to enforce it.");
    }
}
